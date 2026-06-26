using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Nfe.Core;
using Nfe.Api.Models;
using Nfe.Api.Services;
using Nfe.Shared;

namespace Nfe.Api.IntegrationTests;

public sealed class NfeApiIntegrationTests
{
    [Fact]
    public async Task Emitir_DeveRejeitarSemCertificadoSemBaterNaFila()
    {
        await using var factory = new TestApiFactory(new FakeNfeConsultaService());
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/nfe/emitir?gerarDanfe=false");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("certificado", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StatusServico_DeveUsarServicoFalso()
    {
        var fake = new FakeNfeConsultaService
        {
            StatusServicoResult = Result<StatusServicoResult>.Success(new StatusServicoResult
            {
                Status = "107",
                Motivo = "Servico em Operacao",
                Online = true
            })
        };

        await using var factory = new TestApiFactory(fake);
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/nfe/status-servico?uf=SP&ambiente=2");
        request.Headers.Add("X-Certificado-Base64", "qualquer-base64");
        request.Headers.Add("X-Certificado-Senha", "senha");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<StatusServicoResult>();
        Assert.NotNull(body);
        Assert.Equal("107", body!.Status);
        Assert.True(body.Online);
        Assert.Equal(1, fake.StatusServicoCalls);
    }

    [Fact]
    public async Task Consulta_DeveUsarServicoFalso()
    {
        var fake = new FakeNfeConsultaService
        {
            ConsultaResult = Result<ConsultaNfeResult>.Success(new ConsultaNfeResult
            {
                Status = "100",
                Motivo = "Autorizado o uso da NF-e",
                Protocolo = "135000000000000"
            })
        };

        await using var factory = new TestApiFactory(fake);
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/nfe/consulta?chave=35260612345678000195550010000000011000000010&uf=SP&ambiente=2");
        request.Headers.Add("X-Certificado-Base64", "qualquer-base64");
        request.Headers.Add("X-Certificado-Senha", "senha");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ConsultaNfeResult>();
        Assert.NotNull(body);
        Assert.Equal("100", body!.Status);
        Assert.Equal("135000000000000", body.Protocolo);
        Assert.Equal(1, fake.ConsultaCalls);
    }

    [Fact]
    public async Task CertificadoInfo_DeveRetornarDadosDoServicoFalso()
    {
        var fake = new FakeNfeConsultaService
        {
            CertificadoInfoResult = Result<CertificadoInfoResult>.Success(new CertificadoInfoResult
            {
                Cnpj = "12345678000195",
                Nome = "EMPRESA TESTE LTDA",
                DataExpiracao = new DateTime(2030, 12, 31)
            })
        };

        await using var factory = new TestApiFactory(fake);
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/certificado/info");
        request.Headers.Add("X-Certificado-Base64", "qualquer-base64");
        request.Headers.Add("X-Certificado-Senha", "senha");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CertificadoInfoResult>();
        Assert.NotNull(body);
        Assert.Equal("12345678000195", body!.Cnpj);
        Assert.Equal(1, fake.CertificadoInfoCalls);
    }

    [Fact]
    public async Task Schemas_DeveRetornarDiagnosticoQuandoXsdNaoEstaCarregado()
    {
        await using var factory = new TestApiFactory(
            new FakeNfeConsultaService(),
            "/tmp/nfe-schema-ausente");
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/nfe/schemas");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("loaded", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("false", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QueueCredentialProtector_DeveProtegerCredenciaisSemTextoClaro()
    {
        var protector = CriarProtector();
        var credentials = new QueueCredentials
        {
            CertificadoBase64 = "CERTIFICADO_SUPER_SECRETO",
            CertificadoSenha = "SENHA_SUPER_SECRETA",
            CertPemBase64 = "CERT_PEM_SUPER_SECRETO",
            KeyPemBase64 = "KEY_PEM_SUPER_SECRETO"
        };

        var protectedCredentials = protector.Protect(credentials, "corr-123");
        var message = new QueueMessage
        {
            CorrelationId = "corr-123",
            UfEmitente = "SP",
            Ambiente = "2",
            Request = CriarRequestMinimo(),
            CertificadoBase64 = string.Empty,
            CertificadoSenha = string.Empty,
            ProtectedCredentials = protectedCredentials,
            GerarDanfe = false
        };

        var serialized = JsonSerializer.Serialize(message);

        Assert.DoesNotContain(credentials.CertificadoBase64, serialized);
        Assert.DoesNotContain(credentials.CertificadoSenha, serialized);
        Assert.DoesNotContain(credentials.CertPemBase64, serialized);
        Assert.DoesNotContain(credentials.KeyPemBase64, serialized);

        var roundtrip = protector.Unprotect(protectedCredentials, "corr-123");
        Assert.Equal(credentials.CertificadoBase64, roundtrip.CertificadoBase64);
        Assert.Equal(credentials.CertificadoSenha, roundtrip.CertificadoSenha);
        Assert.Equal(credentials.CertPemBase64, roundtrip.CertPemBase64);
        Assert.Equal(credentials.KeyPemBase64, roundtrip.KeyPemBase64);
    }

    [Fact]
    public void QueueCredentialProtector_DeveFalharSeCorrelationIdForAlterado()
    {
        var protector = CriarProtector();
        var protectedCredentials = protector.Protect(new QueueCredentials
        {
            CertificadoBase64 = "CERTIFICADO_SUPER_SECRETO",
            CertificadoSenha = "SENHA_SUPER_SECRETA"
        }, "corr-original");

        Assert.Throws<System.Security.Cryptography.AuthenticationTagMismatchException>(
            () => protector.Unprotect(protectedCredentials, "corr-alterado"));
    }

    private sealed class TestApiFactory : WebApplicationFactory<Program>
    {
        private readonly FakeNfeConsultaService _fakeConsulta;
        private readonly string? _schemasPath;

        public TestApiFactory(FakeNfeConsultaService fakeConsulta, string? schemasPath = null)
        {
            _fakeConsulta = fakeConsulta;
            _schemasPath = schemasPath;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Nfe:SkipSchemaSync", "true");
            builder.UseSetting("Nfe:DisableBackgroundWorker", "true");
            if (!string.IsNullOrWhiteSpace(_schemasPath))
            {
                builder.UseSetting("Nfe:SchemasPath", _schemasPath);
            }

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<INfeConsultaService>();
                services.AddSingleton<INfeConsultaService>(_fakeConsulta);
            });
        }
    }

    private static QueueCredentialProtector CriarProtector()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Nfe:QueueProtectionKey"] = "0123456789abcdef0123456789abcdef"
            })
            .Build();

        return new QueueCredentialProtector(configuration, NullLogger<QueueCredentialProtector>.Instance);
    }

    private static EmitirNfeRequest CriarRequestMinimo() => new()
    {
        AmbienteEmissao = "2",
        Serie = "1",
        NumeroNfe = 1,
        NaturezaOperacao = "VENDA",
        TipoOperacao = "1",
        Emitente = new EmitenteRequest
        {
            Cnpj = "12345678000195",
            RazaoSocial = "EMPRESA TESTE",
            InscricaoEstadual = "110042490114",
            CnaeFiscal = "2500000",
            CodigoRegimeTributario = "3",
            Endereco = new EnderecoRequest
            {
                Logradouro = "RUA TESTE",
                Numero = "1",
                Bairro = "CENTRO",
                CodigoMunicipio = "3550308",
                NomeMunicipio = "SAO PAULO",
                Uf = "SP",
                Cep = "01001000"
            }
        },
        Destinatario = new DestinatarioRequest
        {
            Cnpj = "99999999000191",
            NomeRazaoSocial = "CLIENTE TESTE",
            IndicadorIe = "9",
            Endereco = new EnderecoRequest
            {
                Logradouro = "RUA CLIENTE",
                Numero = "2",
                Bairro = "CENTRO",
                CodigoMunicipio = "3550308",
                NomeMunicipio = "SAO PAULO",
                Uf = "SP",
                Cep = "02002000"
            }
        },
        Produtos = [],
        Transporte = new TransporteRequest { ModalidadeFrete = "9" },
        Pagamentos = []
    };

    private sealed class FakeNfeConsultaService : INfeConsultaService
    {
        public Result<ConsultaNfeResult> ConsultaResult { get; set; } = Result<ConsultaNfeResult>.Failure("NAO_CONFIGURADO", "Consulta nao configurada");
        public Result<StatusServicoResult> StatusServicoResult { get; set; } = Result<StatusServicoResult>.Failure("NAO_CONFIGURADO", "Status nao configurado");
        public Result<CertificadoInfoResult> CertificadoInfoResult { get; set; } = Result<CertificadoInfoResult>.Failure("NAO_CONFIGURADO", "Certificado nao configurado");

        public int ConsultaCalls { get; private set; }
        public int StatusServicoCalls { get; private set; }
        public int CertificadoInfoCalls { get; private set; }

        public Task<Result<ConsultaNfeResult>> ConsultarChaveAsync(string chave, string certBase64, string senha, string ufEmitente, string ambiente, string? certPemBase64 = null, string? keyPemBase64 = null)
        {
            ConsultaCalls++;
            return Task.FromResult(ConsultaResult);
        }

        public Task<Result<StatusServicoResult>> ConsultarStatusServicoAsync(string ufEmitente, string ambiente, string certBase64, string senha, string? certPemBase64 = null, string? keyPemBase64 = null)
        {
            StatusServicoCalls++;
            return Task.FromResult(StatusServicoResult);
        }

        public Result<CertificadoInfoResult> ExtrairInformacoesCertificado(string certBase64, string senha)
        {
            CertificadoInfoCalls++;
            return CertificadoInfoResult;
        }
    }
}
