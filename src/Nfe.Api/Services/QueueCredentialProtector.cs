using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nfe.Api.Models;

namespace Nfe.Api.Services;

public interface IQueueCredentialProtector
{
    ProtectedQueueCredentials Protect(QueueCredentials credentials, string correlationId);
    QueueCredentials Unprotect(ProtectedQueueCredentials protectedCredentials, string correlationId);
}

public sealed class QueueCredentialProtector : IQueueCredentialProtector
{
    private const string Version = "1";
    private const string Algorithm = "AES-256-GCM";
    private const string ConfigKey = "Nfe:QueueProtectionKey";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly byte[] _key;
    private readonly string _keyId;

    public QueueCredentialProtector(IConfiguration configuration, ILogger<QueueCredentialProtector> logger)
    {
        var configuredKey = configuration[ConfigKey];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            _key = RandomNumberGenerator.GetBytes(32);
            _keyId = "ephemeral-local";
            logger.LogWarning(
                "Nfe:QueueProtectionKey não configurada. Usando chave efêmera local para proteger credenciais na fila Redis. Configure Nfe__QueueProtectionKey em produção ou múltiplas réplicas.");
            return;
        }

        _key = DecodeKey(configuredKey);
        _keyId = Convert.ToHexString(SHA256.HashData(_key))[..16].ToLowerInvariant();
    }

    public ProtectedQueueCredentials Protect(QueueCredentials credentials, string correlationId)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(credentials, JsonOptions);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        var aad = BuildAdditionalData(correlationId);

        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);

        CryptographicOperations.ZeroMemory(plaintext);

        return new ProtectedQueueCredentials
        {
            Version = Version,
            Algorithm = Algorithm,
            KeyId = _keyId,
            NonceBase64 = Convert.ToBase64String(nonce),
            CiphertextBase64 = Convert.ToBase64String(ciphertext),
            TagBase64 = Convert.ToBase64String(tag)
        };
    }

    public QueueCredentials Unprotect(ProtectedQueueCredentials protectedCredentials, string correlationId)
    {
        if (protectedCredentials.Version != Version || protectedCredentials.Algorithm != Algorithm)
        {
            throw new InvalidOperationException("Envelope de credenciais da fila em formato incompatível.");
        }

        var nonce = Convert.FromBase64String(protectedCredentials.NonceBase64);
        var ciphertext = Convert.FromBase64String(protectedCredentials.CiphertextBase64);
        var tag = Convert.FromBase64String(protectedCredentials.TagBase64);
        var plaintext = new byte[ciphertext.Length];
        var aad = BuildAdditionalData(correlationId);

        try
        {
            using var aes = new AesGcm(_key, tag.Length);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);
            return JsonSerializer.Deserialize<QueueCredentials>(plaintext, JsonOptions)
                ?? throw new InvalidOperationException("Credenciais descriptografadas estão vazias.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] DecodeKey(string configuredKey)
    {
        var value = configuredKey.Trim();

        if (value.StartsWith("base64:", StringComparison.OrdinalIgnoreCase))
        {
            value = value["base64:".Length..];
        }

        try
        {
            var decoded = Convert.FromBase64String(value);
            if (decoded.Length is 32)
            {
                return decoded;
            }
        }
        catch (FormatException)
        {
            // Continua para derivar por SHA-256 a partir do segredo textual.
        }

        if (value.Length < 32)
        {
            throw new InvalidOperationException("Nfe:QueueProtectionKey deve ter ao menos 32 caracteres ou ser Base64 de 32 bytes.");
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(value));
    }

    private static byte[] BuildAdditionalData(string correlationId)
        => Encoding.UTF8.GetBytes($"nfe-emissao-queue:{correlationId}");
}
