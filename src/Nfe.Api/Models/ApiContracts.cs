using System.Text.Json.Serialization;
using Nfe.Shared;

namespace Nfe.Api.Models;

public sealed record EmitirNfeApiResponse
{
    public required string CorrelationId { get; init; }
    public required string Status { get; init; }
    public string? Message { get; init; }
}

public sealed record NfeStatusApiResponse
{
    public required string CorrelationId { get; init; }
    public required string Status { get; init; } // "Pendente", "Processando", "Autorizada", "Rejeitada", "Erro"
    public string? ChaveAcesso { get; init; }
    public string? Protocolo { get; init; }
    public string? XmlResult { get; init; } // XmlProcNfe completo em caso de autorização
    public string? DanfePdfBase64 { get; init; }
    public string? ErroDetalhado { get; init; }
    public SefazErroApiResponse? SefazErro { get; init; }
    public DateTimeOffset? ExpiraEm { get; init; }
    public int? TtlSegundos { get; init; }
    public StorageResult? Storage { get; init; }
}

public sealed class QueueMessage
{
    public required string CorrelationId { get; init; }
    public required string UfEmitente { get; init; }
    public required string Ambiente { get; init; }
    public required EmitirNfeRequest Request { get; init; }
    public required string CertificadoBase64 { get; init; } = string.Empty;
    public required string CertificadoSenha { get; init; } = string.Empty;

    // Alternativa PEM: se preenchidos, sobrepoe o PFX acima
    /// <summary>Conteudo do cert.pem codificado em Base64 (opcional, alternativa ao PFX)</summary>
    public string? CertPemBase64 { get; init; }
    /// <summary>Conteudo do key.pem codificado em Base64 (opcional, alternativa ao PFX)</summary>
    public string? KeyPemBase64 { get; init; }
    public ProtectedQueueCredentials? ProtectedCredentials { get; init; }
    public bool GerarDanfe { get; init; }
}

public sealed record QueueCredentials
{
    public string CertificadoBase64 { get; init; } = string.Empty;
    public string CertificadoSenha { get; init; } = string.Empty;
    public string? CertPemBase64 { get; init; }
    public string? KeyPemBase64 { get; init; }
}

public sealed record ProtectedQueueCredentials
{
    public required string Version { get; init; }
    public required string Algorithm { get; init; }
    public required string KeyId { get; init; }
    public required string NonceBase64 { get; init; }
    public required string CiphertextBase64 { get; init; }
    public required string TagBase64 { get; init; }
}

public sealed record StorageResult
{
    public bool Persistido { get; init; }
    public string? XmlProcNfeUri { get; init; }
    public string? DanfePdfUri { get; init; }
}

public sealed record SefazErroApiResponse
{
    public string? Codigo { get; init; }
    public string? Status { get; init; }
    public string? Motivo { get; init; }
    public string? ChaveAcesso { get; init; }
    public string? Protocolo { get; init; }
}
