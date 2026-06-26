using System.Text.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nfe.Api.Models;
using Nfe.Api.Services;
using Nfe.Core;
using Nfe.Shared;
using NFeSchemaDownloader;
using QuestPDF.Infrastructure;
using Serilog;
using StackExchange.Redis;

ConfigurarLicencaQuestPdf();

var builder = WebApplication.CreateBuilder(args);

// Configuração do Serilog estruturado com Seq
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.Seq(ctx.Configuration["Seq:Url"] ?? "http://localhost:5345"));

// Forçar desserialização JSON case-insensitive nas Minimal APIs para evitar 400 Bad Request
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

// Registra dependências do Core
builder.Services.AddScoped<INfeSefazService, NfeSefazService>();
builder.Services.AddScoped<INfeXmlBuilder, NfeXmlBuilder>();
builder.Services.AddScoped<INfeAssinadorService, NfeAssinadorService>();
builder.Services.AddScoped<INfeConsultaService, NfeConsultaService>();
builder.Services.AddScoped<INfeEventoService, NfeEventoService>();
builder.Services.AddSingleton<INfeStorage, NoopNfeStorage>();
builder.Services.AddSingleton<IQueueCredentialProtector, QueueCredentialProtector>();
builder.Services.AddSingleton<NfeXsdValidator>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var schemasDir = configuration["Nfe:SchemasPath"] ?? Path.Combine(AppContext.BaseDirectory, "schemas", "v4");
    if (!Directory.Exists(schemasDir)) Directory.CreateDirectory(schemasDir);
    return new NfeXsdValidator(
        schemasDir,
        configuration["Nfe:TiposBasicosSchema"],
        configuration["Nfe:NfeSchema"]);
});

// Registra Redis
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var conn = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(conn);
});
builder.Services
    .AddHealthChecks()
    .AddCheck<RedisHealthCheck>("redis");

// Registra o Worker em Background da fila do Redis, exceto em cenários de teste/integracao controlada
if (!builder.Configuration.GetValue<bool>("Nfe:DisableBackgroundWorker"))
{
    builder.Services.AddHostedService<NfeQueueWorker>();
}

// Registra Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSerilogRequestLogging();

app.MapHealthChecks("/health");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Startup task: Schemas XSD da SEFAZ
var skipSchemaSync = builder.Configuration.GetValue<bool>("Nfe:SkipSchemaSync");
var schemasDir = builder.Configuration["Nfe:SchemasPath"] ?? Path.Combine(AppContext.BaseDirectory, "schemas", "v4");
if (!skipSchemaSync && (!Directory.Exists(schemasDir) || !Directory.EnumerateFiles(schemasDir, "*.xsd").Any()))
{
    try
    {
        Log.Information("Pasta schemas/v4 vazia ou inexistente. Iniciando sincronização via NFeSchemaDownloader...");
        if (!Directory.Exists(schemasDir)) Directory.CreateDirectory(schemasDir);
        await NFeSchemaManager.SyncSchemasAsync();
        Log.Information("Schemas XSD sincronizados com sucesso.");
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Não foi possível rodar o NFeSchemaDownloader para baixar os schemas iniciais.");
    }
}
else if (!skipSchemaSync)
{
    Log.Information("Schemas XSD detectados localmente em: {SchemasDir}. Ignorando download automático no startup.", schemasDir);
}

var api = app.MapGroup("/api/v1");

// 1. Endpoint assíncrono para emissão de nota
api.MapPost("/nfe/emitir", async (
    HttpContext context,
    [FromHeader(Name = "X-Certificado-Base64")] string? certHeader,
    [FromHeader(Name = "X-Certificado-Senha")] string? senhaHeader,
    [FromHeader(Name = "X-Cert-Pem-Base64")] string? certPemHeader,
    [FromHeader(Name = "X-Key-Pem-Base64")] string? keyPemHeader,
    [FromQuery] bool gerarDanfe,
    ILogger<Program> logger,
    IQueueCredentialProtector credentialProtector) =>
{
    var cert = certHeader;
    var senha = senhaHeader ?? string.Empty;

    var usandoPem = !string.IsNullOrWhiteSpace(certPemHeader) && !string.IsNullOrWhiteSpace(keyPemHeader);

    if (string.IsNullOrWhiteSpace(cert) && !usandoPem)
    {
        return Results.BadRequest(new { Error = "Informe o certificado via header X-Certificado-Base64 (PFX) OU via X-Cert-Pem-Base64 + X-Key-Pem-Base64 (PEM)." });
    }

    // Leitura manual do body para capturar exceções detalhadas de desserialização
    EmitirNfeRequest? request;
    try
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        request = await JsonSerializer.DeserializeAsync<EmitirNfeRequest>(context.Request.Body, options);
        if (request == null)
        {
            return Results.BadRequest(new { Error = "Payload JSON nulo ou inválido." });
        }
    }
    catch (JsonException jsonEx)
    {
        logger.LogError(jsonEx, "Erro ao desserializar EmitirNfeRequest.");
        return Results.BadRequest(new { Error = "JSON inválido na estrutura de NF-e.", Details = jsonEx.Message });
    }

    var validation = NfeRequestRules.NormalizeAndValidate(request);
    if (!validation.IsSuccess)
    {
        return Results.BadRequest(new { Error = "NF-e inválida para envio.", Code = validation.ErrorCode, Details = validation.ErrorMessage });
    }

    request = validation.Value!;
    var redis = context.RequestServices.GetRequiredService<IConnectionMultiplexer>();
    var db = redis.GetDatabase();
    var backoffKey = ObterSefazBackoffKey(request.Emitente.Endereco.Uf, request.AmbienteEmissao);
    var backoff = await db.StringGetAsync(backoffKey);
    if (!backoff.IsNullOrEmpty)
    {
        return Results.Json(
            new { Error = "Envio temporariamente bloqueado para evitar consumo indevido na SEFAZ.", Details = backoff.ToString() },
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    var correlationId = Guid.NewGuid().ToString("N");
    logger.LogInformation("Recebida requisição de NF-e. Gerando ID de correlação: {CorrId}", correlationId);

    // Lock de Idempotência
    var idempotencyKey = $"nfe-idemp:{request.Emitente.Cnpj.OnlyAlphaNumericUpper()}:{request.Serie}:{request.NumeroNfe}";
    var setLock = await db.StringSetAsync(idempotencyKey, correlationId, TimeSpan.FromMinutes(5), When.NotExists);

    if (!setLock)
    {
        var existingCorrId = await db.StringGetAsync(idempotencyKey);
        logger.LogWarning("Duplicidade detectada! Requisição idêntica já postada. correlationId existente: {ExistingId}", existingCorrId);
        return Results.Conflict(new { Error = "Esta nota já foi enviada para processamento.", CorrelationId = existingCorrId.ToString() });
    }

    var protectedCredentials = credentialProtector.Protect(new QueueCredentials
    {
        CertificadoBase64 = cert ?? string.Empty,
        CertificadoSenha = senha,
        CertPemBase64 = certPemHeader,
        KeyPemBase64 = keyPemHeader
    }, correlationId);

    // Cria a mensagem sem credenciais fiscais em claro na fila Redis.
    var msg = new QueueMessage
    {
        CorrelationId = correlationId,
        UfEmitente = request.Emitente.Endereco.Uf,
        Ambiente = request.AmbienteEmissao,
        Request = request,
        CertificadoBase64 = string.Empty,
        CertificadoSenha = string.Empty,
        CertPemBase64 = null,
        KeyPemBase64 = null,
        ProtectedCredentials = protectedCredentials,
        GerarDanfe = gerarDanfe
    };

    var statusKey = $"nfe-status:{correlationId}";
    var statusTtl = NfeQueueWorker.StatusTtl;
    var initialStatus = new NfeStatusApiResponse
    {
        CorrelationId = correlationId,
        Status = "Pendente",
        ExpiraEm = DateTimeOffset.UtcNow.Add(statusTtl),
        TtlSegundos = (int)statusTtl.TotalSeconds
    };
    await db.StringSetAsync(statusKey, JsonSerializer.Serialize(initialStatus), statusTtl);

    await db.ListLeftPushAsync("nfe-emissao-queue", JsonSerializer.Serialize(msg));

    return Results.Accepted($"/api/v1/nfe/status/{correlationId}", new EmitirNfeApiResponse
    {
        CorrelationId = correlationId,
        Status = "Pendente",
        Message = "A nota fiscal foi colocada na fila de processamento."
    });
});

api.MapGet("/nfe/status/{correlationId}", async (string correlationId, IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();
    var statusKey = $"nfe-status:{correlationId}";
    var data = await db.StringGetAsync(statusKey);

    if (data.IsNullOrEmpty) return Results.NotFound(new { Error = "ID de correlação não encontrado ou expirado." });

    var res = JsonSerializer.Deserialize<NfeStatusApiResponse>(data.ToString());
    return Results.Ok(res);
});

api.MapGet("/nfe/status-servico", async (
    [FromQuery] string uf,
    [FromQuery] string ambiente,
    [FromHeader(Name = "X-Certificado-Base64")] string? certHeader,
    [FromHeader(Name = "X-Certificado-Senha")] string? senhaHeader,
    [FromHeader(Name = "X-Cert-Pem-Base64")] string? certPemHeader,
    [FromHeader(Name = "X-Key-Pem-Base64")] string? keyPemHeader,
    INfeConsultaService consultaService) =>
{
    var certResult = PrepararCertificadoConsulta(certHeader, senhaHeader, certPemHeader, keyPemHeader);
    if (!certResult.IsSuccess)
    {
        return Results.BadRequest(new { Error = certResult.ErrorMessage });
    }

    var cert = certResult.Value!;
    var res = await consultaService.ConsultarStatusServicoAsync(uf, ambiente, cert.CertificadoBase64, cert.Senha);
    return res.IsSuccess ? Results.Ok(res.Value) : Results.UnprocessableEntity(res);
});

api.MapGet("/nfe/schemas", (IServiceProvider services, IConfiguration configuration) =>
{
    var schemasDir = configuration["Nfe:SchemasPath"] ?? Path.Combine(AppContext.BaseDirectory, "schemas", "v4");
    var validateBeforeSend = configuration.GetValue("Nfe:ValidateXsdBeforeSend", true);

    try
    {
        var validator = services.GetRequiredService<NfeXsdValidator>();
        return Results.Ok(new
        {
            schemasPath = validator.SchemasPath,
            tiposBasicosSchema = validator.TiposBasicosFileName,
            nfeSchema = validator.NfeFileName,
            validateXsdBeforeSend = validateBeforeSend,
            loaded = true
        });
    }
    catch (Exception ex)
    {
        return Results.Json(
            new
            {
                schemasPath = schemasDir,
                tiposBasicosSchema = configuration["Nfe:TiposBasicosSchema"],
                nfeSchema = configuration["Nfe:NfeSchema"],
                validateXsdBeforeSend = validateBeforeSend,
                loaded = false,
                error = ex.Message
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

api.MapGet("/nfe/consulta", async (
    [FromQuery] string chave,
    [FromQuery] string uf,
    [FromQuery] string ambiente,
    [FromHeader(Name = "X-Certificado-Base64")] string? certHeader,
    [FromHeader(Name = "X-Certificado-Senha")] string? senhaHeader,
    [FromHeader(Name = "X-Cert-Pem-Base64")] string? certPemHeader,
    [FromHeader(Name = "X-Key-Pem-Base64")] string? keyPemHeader,
    INfeConsultaService consultaService) =>
{
    chave = chave.OnlyAlphaNumericUpper();
    if (string.IsNullOrWhiteSpace(chave) || chave.Length != 44)
    {
        return Results.BadRequest(new { Error = "Informe uma chave NF-e válida com 44 posições alfanuméricas." });
    }

    var certResult = PrepararCertificadoConsulta(certHeader, senhaHeader, certPemHeader, keyPemHeader);
    if (!certResult.IsSuccess)
    {
        return Results.BadRequest(new { Error = certResult.ErrorMessage });
    }

    var cert = certResult.Value!;
    var res = await consultaService.ConsultarChaveAsync(chave, cert.CertificadoBase64, cert.Senha, uf, ambiente);
    return res.IsSuccess ? Results.Ok(res.Value) : Results.UnprocessableEntity(res);
});

api.MapPost("/nfe/cancelar", async (
    CancelarNfeRequest request,
    [FromHeader(Name = "X-Certificado-Base64")] string? certHeader,
    [FromHeader(Name = "X-Certificado-Senha")] string? senhaHeader,
    [FromHeader(Name = "X-Cert-Pem-Base64")] string? certPemHeader,
    [FromHeader(Name = "X-Key-Pem-Base64")] string? keyPemHeader,
    INfeEventoService eventoService,
    CancellationToken ct) =>
{
    var certResult = PrepararCertificadoConsulta(certHeader, senhaHeader, certPemHeader, keyPemHeader);
    if (!certResult.IsSuccess)
    {
        return Results.BadRequest(new { Error = certResult.ErrorMessage });
    }

    var cert = certResult.Value!;
    var res = await eventoService.CancelarAsync(
        request,
        cert.CertificadoBase64,
        cert.Senha,
        certPemHeader,
        keyPemHeader,
        ct);

    return res.IsSuccess ? Results.Ok(res.Value) : Results.UnprocessableEntity(res);
});

api.MapPost("/nfe/cce", async (
    CartaCorrecaoRequest request,
    [FromHeader(Name = "X-Certificado-Base64")] string? certHeader,
    [FromHeader(Name = "X-Certificado-Senha")] string? senhaHeader,
    [FromHeader(Name = "X-Cert-Pem-Base64")] string? certPemHeader,
    [FromHeader(Name = "X-Key-Pem-Base64")] string? keyPemHeader,
    INfeEventoService eventoService,
    CancellationToken ct) =>
{
    var certResult = PrepararCertificadoConsulta(certHeader, senhaHeader, certPemHeader, keyPemHeader);
    if (!certResult.IsSuccess)
    {
        return Results.BadRequest(new { Error = certResult.ErrorMessage });
    }

    var cert = certResult.Value!;
    var res = await eventoService.RegistrarCartaCorrecaoAsync(
        request,
        cert.CertificadoBase64,
        cert.Senha,
        certPemHeader,
        keyPemHeader,
        ct);

    return res.IsSuccess ? Results.Ok(res.Value) : Results.UnprocessableEntity(res);
});

api.MapPost("/nfe/inutilizar", async (
    InutilizarNumeracaoRequest request,
    [FromHeader(Name = "X-Certificado-Base64")] string? certHeader,
    [FromHeader(Name = "X-Certificado-Senha")] string? senhaHeader,
    [FromHeader(Name = "X-Cert-Pem-Base64")] string? certPemHeader,
    [FromHeader(Name = "X-Key-Pem-Base64")] string? keyPemHeader,
    INfeEventoService eventoService,
    CancellationToken ct) =>
{
    var certResult = PrepararCertificadoConsulta(certHeader, senhaHeader, certPemHeader, keyPemHeader);
    if (!certResult.IsSuccess)
    {
        return Results.BadRequest(new { Error = certResult.ErrorMessage });
    }

    var cert = certResult.Value!;
    var res = await eventoService.InutilizarAsync(
        request,
        cert.CertificadoBase64,
        cert.Senha,
        certPemHeader,
        keyPemHeader,
        ct);

    return res.IsSuccess ? Results.Ok(res.Value) : Results.UnprocessableEntity(res);
});

api.MapPost("/certificado/info", (
    [FromHeader(Name = "X-Certificado-Base64")] string cert,
    [FromHeader(Name = "X-Certificado-Senha")] string? senha,
    INfeConsultaService consultaService) =>
{
    var res = consultaService.ExtrairInformacoesCertificado(cert, senha ?? string.Empty);
    return res.IsSuccess ? Results.Ok(res.Value) : Results.UnprocessableEntity(res);
});

await app.RunAsync();

static string ObterSefazBackoffKey(string uf, string ambiente)
    => $"nfe-sefaz-backoff:{uf.ToUpperInvariant()}:{ambiente}";

static void ConfigurarLicencaQuestPdf()
{
    QuestPDF.Settings.License = LicenseType.Community;
}

static Result<CertificadoConsulta> PrepararCertificadoConsulta(
    string? certBase64,
    string? senha,
    string? certPemBase64,
    string? keyPemBase64)
{
    if (!string.IsNullOrWhiteSpace(certBase64))
    {
        return Result<CertificadoConsulta>.Success(new CertificadoConsulta(certBase64, senha ?? string.Empty));
    }

    if (string.IsNullOrWhiteSpace(certPemBase64) || string.IsNullOrWhiteSpace(keyPemBase64))
    {
        return Result<CertificadoConsulta>.Failure(
            "CERTIFICADO_OBRIGATORIO",
            "Informe X-Certificado-Base64 ou X-Cert-Pem-Base64 + X-Key-Pem-Base64.");
    }

    try
    {
        var certPem = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(certPemBase64));
        var keyPem = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(keyPemBase64));

        using var publicCert = X509CertificateLoader.LoadCertificate(System.Text.Encoding.UTF8.GetBytes(certPem));
        using var rsa = RSA.Create();
        rsa.ImportFromPem(keyPem);
        using var cert = publicCert.CopyWithPrivateKey(rsa);
        var pfxBase64 = Convert.ToBase64String(cert.Export(X509ContentType.Pkcs12));

        return Result<CertificadoConsulta>.Success(new CertificadoConsulta(pfxBase64, string.Empty));
    }
    catch (Exception ex)
    {
        return Result<CertificadoConsulta>.Failure("CERTIFICADO_PEM_INVALIDO", ex.Message);
    }
}

sealed record CertificadoConsulta(string CertificadoBase64, string Senha);
