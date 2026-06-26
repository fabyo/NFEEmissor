using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using NFEConsulta.Models;
using NFEConsulta.Services;
using Nfe.Shared;

namespace Nfe.Core;

public interface INfeConsultaService
{
    Task<Result<ConsultaNfeResult>> ConsultarChaveAsync(string chave, string certBase64, string senha, string ufEmitente, string ambiente, string? certPemBase64 = null, string? keyPemBase64 = null);
    Task<Result<StatusServicoResult>> ConsultarStatusServicoAsync(string ufEmitente, string ambiente, string certBase64, string senha, string? certPemBase64 = null, string? keyPemBase64 = null);
    Result<CertificadoInfoResult> ExtrairInformacoesCertificado(string certBase64, string senha);
}

public sealed class ConsultaNfeResult
{
    public required string Status { get; init; }
    public required string Motivo { get; init; }
    public string? Protocolo { get; init; }
    public string? XmlRetorno { get; init; }
}

public sealed class StatusServicoResult
{
    public required string Status { get; init; }
    public required string Motivo { get; init; }
    public bool Online { get; init; }
}

public sealed class CertificadoInfoResult
{
    public required string Cnpj { get; init; }
    public required string Nome { get; init; }
    public required DateTime DataExpiracao { get; init; }
}

public sealed class NfeConsultaService : INfeConsultaService
{
    public async Task<Result<ConsultaNfeResult>> ConsultarChaveAsync(string chave, string certBase64, string senha, string ufEmitente, string ambiente, string? certPemBase64 = null, string? keyPemBase64 = null)
    {
        try
        {
            using var cert = CarregarCertificadoCliente(certBase64, senha, certPemBase64, keyPemBase64);

            var options = new NFeConsultaOptions
            {
                Ambiente = ambiente == "1" ? TipoAmbiente.Producao : TipoAmbiente.Homologacao,
                Uf = Enum.Parse<UfNFe>(ufEmitente, true)
            };

            using var client = NFeConsultaClient.CriarComCertificado(cert, options);
            var res = await client.ConsultarChaveAsync(chave);

            return Result<ConsultaNfeResult>.Success(new ConsultaNfeResult
            {
                Status = res.CodigoStatus,
                Motivo = res.Motivo,
                Protocolo = res.NumeroProtocolo,
                XmlRetorno = null
            });
        }
        catch (Exception ex)
        {
            return Result<ConsultaNfeResult>.Failure("CONSULTA_SEFAZ_FALHOU", ex.Message);
        }
    }

    public async Task<Result<StatusServicoResult>> ConsultarStatusServicoAsync(string ufEmitente, string ambiente, string certBase64, string senha, string? certPemBase64 = null, string? keyPemBase64 = null)
    {
        try
        {
            using var cert = CarregarCertificadoCliente(certBase64, senha, certPemBase64, keyPemBase64);

            var options = new NFeConsultaOptions
            {
                Ambiente = ambiente == "1" ? TipoAmbiente.Producao : TipoAmbiente.Homologacao,
                Uf = Enum.Parse<UfNFe>(ufEmitente, true)
            };

            using var client = NFeStatusClient.CriarComCertificado(cert, options);
            var res = await client.ConsultarStatusAsync();

            return Result<StatusServicoResult>.Success(new StatusServicoResult
            {
                Status = res.CodigoStatus,
                Motivo = res.Motivo,
                Online = res.ServicoEmOperacao
            });
        }
        catch (Exception ex)
        {
            return Result<StatusServicoResult>.Failure("STATUS_SERVICO_FALHOU", ex.Message);
        }
    }

    public Result<CertificadoInfoResult> ExtrairInformacoesCertificado(string certBase64, string senha)
    {
        try
        {
            var certBytes = Convert.FromBase64String(certBase64);
            using var cert = X509CertificateLoader.LoadPkcs12(
                certBytes,
                senha,
                X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable,
                new Pkcs12LoaderLimits());

            var subject = cert.Subject;
            var cnpj = string.Empty;
            var nome = cert.FriendlyName;

            if (string.IsNullOrEmpty(nome))
            {
                var cnMatch = Regex.Match(subject, @"CN=([^,]+)");
                nome = cnMatch.Success ? cnMatch.Groups[1].Value.Trim() : subject;
            }

            var cnpjMatch = Regex.Match(subject, @"(?i)CNPJ:(\d{14})");
            if (cnpjMatch.Success)
            {
                cnpj = cnpjMatch.Groups[1].Value;
            }
            else
            {
                var matchDigits = Regex.Match(nome, @"\d{14}");
                if (matchDigits.Success)
                {
                    cnpj = matchDigits.Value;
                }
            }

            return Result<CertificadoInfoResult>.Success(new CertificadoInfoResult
            {
                Cnpj = cnpj,
                Nome = nome,
                DataExpiracao = cert.NotAfter
            });
        }
        catch (Exception ex)
        {
            return Result<CertificadoInfoResult>.Failure("EXTRAIR_CERTIFICADO_FALHOU", ex.Message);
        }
    }

    private static X509Certificate2 CarregarCertificadoCliente(string certBase64, string senha, string? certPemBase64, string? keyPemBase64)
    {
        if (!string.IsNullOrWhiteSpace(certPemBase64) && !string.IsNullOrWhiteSpace(keyPemBase64))
        {
            var certPem = Encoding.UTF8.GetString(Convert.FromBase64String(certPemBase64));
            var keyPem = Encoding.UTF8.GetString(Convert.FromBase64String(keyPemBase64));
            using var publicCert = X509CertificateLoader.LoadCertificate(Encoding.UTF8.GetBytes(certPem));
            using var rsa = RSA.Create();
            rsa.ImportFromPem(keyPem);
            return publicCert.CopyWithPrivateKey(rsa);
        }

        var certBytes = Convert.FromBase64String(certBase64);
        return X509CertificateLoader.LoadPkcs12(
            certBytes,
            senha,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable,
            new Pkcs12LoaderLimits());
    }
}
