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
    public required string CertificadoBase64 { get; init; }
    public required string CertificadoSenha { get; init; }

    // Alternativa PEM: se preenchidos, sobrepoe o PFX acima
    /// <summary>Conteudo do cert.pem codificado em Base64 (opcional, alternativa ao PFX)</summary>
    public string? CertPemBase64 { get; init; }
    /// <summary>Conteudo do key.pem codificado em Base64 (opcional, alternativa ao PFX)</summary>
    public string? KeyPemBase64 { get; init; }
    public bool GerarDanfe { get; init; }
}

public sealed record StorageResult
{
    public bool Persistido { get; init; }
    public string? XmlProcNfeUri { get; init; }
    public string? DanfePdfUri { get; init; }
}
