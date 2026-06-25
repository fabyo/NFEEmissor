using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nfe.Api.Models;
using Nfe.Core;
using NFEDanfe;
using StackExchange.Redis;

namespace Nfe.Api.Services;

public sealed class NfeQueueWorker : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NfeQueueWorker> _logger;

    private const string QueueName = "nfe-emissao-queue";
    private const string StatusKeyPrefix = "nfe-status:";
    public static readonly TimeSpan StatusTtl = TimeSpan.FromHours(12);
    private static readonly TimeSpan SefazConsumoIndevidoBackoff = TimeSpan.FromMinutes(10);

    public NfeQueueWorker(
        IConnectionMultiplexer redis,
        IServiceProvider serviceProvider,
        ILogger<NfeQueueWorker> logger)
    {
        _redis = redis;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NfeQueueWorker iniciado e aguardando fila Redis...");

        var db = _redis.GetDatabase();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // BRPOP remove o último elemento da lista de forma bloqueante (economiza CPU)
                var result = await db.ListRightPopAsync(QueueName);

                if (result.IsNullOrEmpty)
                {
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                var messageJson = result.ToString();
                var msg = JsonSerializer.Deserialize<QueueMessage>(messageJson);

                if (msg == null) continue;

                _logger.LogInformation("Processando nota na fila — CorrelationId: {CorrId}", msg.CorrelationId);

                await ProcessarMensagemAsync(db, msg, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro crítico no loop do Worker de fila do Redis");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task ProcessarMensagemAsync(IDatabase db, QueueMessage msg, CancellationToken ct)
    {
        var statusKey = $"{StatusKeyPrefix}{msg.CorrelationId}";

        try
        {
            // Atualiza status para Processando
            await AtualizarStatusRedisAsync(db, statusKey, "Processando");

            var backoffKey = ObterSefazBackoffKey(msg.UfEmitente, msg.Ambiente);
            var backoff = await db.StringGetAsync(backoffKey);
            if (!backoff.IsNullOrEmpty)
            {
                await FinalizarComErroAsync(
                    db,
                    statusKey,
                    "SEFAZ_BACKOFF",
                    $"Envio bloqueado temporariamente para evitar consumo indevido na SEFAZ. {backoff}");
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var xmlBuilder = scope.ServiceProvider.GetRequiredService<INfeXmlBuilder>();
            var assinador = scope.ServiceProvider.GetRequiredService<INfeAssinadorService>();
            var sefaz = scope.ServiceProvider.GetRequiredService<INfeSefazService>();
            var storage = scope.ServiceProvider.GetRequiredService<INfeStorage>();

            // 1. Gera XML
            var xmlBuildResult = await xmlBuilder.BuildAsync(msg.Request, ct);
            if (!xmlBuildResult.IsSuccess)
            {
                await FinalizarComErroAsync(db, statusKey, xmlBuildResult.ErrorCode!, xmlBuildResult.ErrorMessage!);
                return;
            }

            var buildVal = xmlBuildResult.Value!;

            // 2. Assina — usa PEM se disponivel, caso contrario usa PFX (Base64)
            var usandoPem = !string.IsNullOrWhiteSpace(msg.CertPemBase64) && !string.IsNullOrWhiteSpace(msg.KeyPemBase64);
            var assResult = usandoPem
                ? assinador.AssinarComPemConteudo(
                    buildVal.XmlNfe,
                    System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(msg.CertPemBase64!)),
                    System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(msg.KeyPemBase64!)))
                : assinador.Assinar(buildVal.XmlNfe, msg.CertificadoBase64, msg.CertificadoSenha);

            _logger.LogInformation("Assinando com {Modo}. CorrelationId: {CorrId}",
                usandoPem ? "PEM" : "PFX", msg.CorrelationId);
            if (!assResult.IsSuccess)
            {
                await FinalizarComErroAsync(db, statusKey, assResult.ErrorCode!, assResult.ErrorMessage!);
                return;
            }

            var assVal = assResult.Value!;

            // 3. Envia para a SEFAZ
            var sefazResult = await sefaz.AutorizarAsync(
                assVal.XmlAssinado,
                msg.UfEmitente,
                msg.Ambiente,
                msg.CertificadoBase64,
                msg.CertificadoSenha,
                msg.CertPemBase64,
                msg.KeyPemBase64,
                ct);
            if (!sefazResult.IsSuccess)
            {
                if (sefazResult.ErrorCode == "SEFAZ_656")
                {
                    await db.StringSetAsync(
                        backoffKey,
                        $"{sefazResult.ErrorMessage} Aguarde {SefazConsumoIndevidoBackoff.TotalMinutes:0} minutos antes de reenviar.",
                        SefazConsumoIndevidoBackoff);
                }

                await FinalizarComErroAsync(db, statusKey, sefazResult.ErrorCode!, sefazResult.ErrorMessage!);
                return;
            }

            var sefazVal = sefazResult.Value!;

            // 4. Se solicitado, gera o DANFE em PDF
            string? danfePdfBase64 = null;
            byte[]? danfePdf = null;
            if (msg.GerarDanfe)
            {
                try
                {
                    using var pdfStream = new MemoryStream();
                    DanfeGenerator.GenerateFromXmlContent(sefazVal.XmlProcNfe, pdfStream);
                    danfePdf = pdfStream.ToArray();
                    danfePdfBase64 = Convert.ToBase64String(danfePdf);
                }
                catch (Exception pdfEx)
                {
                    _logger.LogWarning(pdfEx, "NF-e autorizada com sucesso, mas a geração do DANFE falhou.");
                }
            }

            var storageResult = await storage.SaveAsync(new NfeStorageDocument
            {
                CorrelationId = msg.CorrelationId,
                Ambiente = msg.Ambiente,
                ChaveAcesso = assVal.ChaveAcesso,
                XmlProcNfe = sefazVal.XmlProcNfe,
                DanfePdf = danfePdf
            }, ct);

            // 5. Finaliza com sucesso
            var statusData = new NfeStatusApiResponse
            {
                CorrelationId = msg.CorrelationId,
                Status = "Autorizada",
                ChaveAcesso = assVal.ChaveAcesso,
                Protocolo = sefazVal.Protocolo,
                XmlResult = sefazVal.XmlProcNfe,
                DanfePdfBase64 = danfePdfBase64,
                ExpiraEm = DateTimeOffset.UtcNow.Add(StatusTtl),
                TtlSegundos = (int)StatusTtl.TotalSeconds,
                Storage = new StorageResult
                {
                    Persistido = storageResult.Persistido,
                    XmlProcNfeUri = storageResult.XmlProcNfeUri,
                    DanfePdfUri = storageResult.DanfePdfUri
                }
            };

            await db.StringSetAsync(statusKey, JsonSerializer.Serialize(statusData), StatusTtl);
            _logger.LogInformation("NF-e autorizada com sucesso! CorrelationId: {CorrId} | Chave: {Chave}", msg.CorrelationId, assVal.ChaveAcesso);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar nota fiscal. CorrelationId: {CorrId}", msg.CorrelationId);
            await FinalizarComErroAsync(db, statusKey, "ERRO_INTERNO", ex.Message);
        }
    }

    private static async Task AtualizarStatusRedisAsync(IDatabase db, string key, string status)
    {
        var data = new NfeStatusApiResponse
        {
            CorrelationId = key.Replace(StatusKeyPrefix, ""),
            Status = status,
            ExpiraEm = DateTimeOffset.UtcNow.Add(StatusTtl),
            TtlSegundos = (int)StatusTtl.TotalSeconds
        };
        await db.StringSetAsync(key, JsonSerializer.Serialize(data), StatusTtl);
    }

    private async Task FinalizarComErroAsync(IDatabase db, string key, string code, string message)
    {
        _logger.LogError("Falha no processamento. Codigo: {Code} | Mensagem: {Msg}", code, message);

        var data = new NfeStatusApiResponse
        {
            CorrelationId = key.Replace(StatusKeyPrefix, ""),
            Status = "Rejeitada",
            ErroDetalhado = $"[{code}] {message}",
            ExpiraEm = DateTimeOffset.UtcNow.Add(StatusTtl),
            TtlSegundos = (int)StatusTtl.TotalSeconds
        };
        await db.StringSetAsync(key, JsonSerializer.Serialize(data), StatusTtl);
    }

    private static string ObterSefazBackoffKey(string uf, string ambiente)
        => $"nfe-sefaz-backoff:{uf.ToUpperInvariant()}:{ambiente}";
}
