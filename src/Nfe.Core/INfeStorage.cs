namespace Nfe.Core;

public interface INfeStorage
{
    Task<NfeStorageResult> SaveAsync(NfeStorageDocument document, CancellationToken ct = default);
}

public sealed record NfeStorageDocument
{
    public required string CorrelationId { get; init; }
    public required string Ambiente { get; init; }
    public required string ChaveAcesso { get; init; }
    public required string XmlProcNfe { get; init; }
    public byte[]? DanfePdf { get; init; }
}

public sealed record NfeStorageResult
{
    public bool Persistido { get; init; }
    public string? XmlProcNfeUri { get; init; }
    public string? DanfePdfUri { get; init; }
}

public sealed class NoopNfeStorage : INfeStorage
{
    public Task<NfeStorageResult> SaveAsync(NfeStorageDocument document, CancellationToken ct = default)
        => Task.FromResult(new NfeStorageResult { Persistido = false });
}
