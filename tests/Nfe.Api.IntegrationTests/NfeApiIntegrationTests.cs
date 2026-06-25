using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nfe.Core;
using Nfe.Api.Models;
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

    private sealed class TestApiFactory : WebApplicationFactory<Program>
    {
        private readonly FakeNfeConsultaService _fakeConsulta;

        public TestApiFactory(FakeNfeConsultaService fakeConsulta)
        {
            _fakeConsulta = fakeConsulta;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Nfe:SkipSchemaSync", "true");
            builder.UseSetting("Nfe:DisableBackgroundWorker", "true");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<INfeConsultaService>();
                services.AddSingleton<INfeConsultaService>(_fakeConsulta);
            });
        }
    }

    private sealed class FakeNfeConsultaService : INfeConsultaService
    {
        public Result<ConsultaNfeResult> ConsultaResult { get; set; } = Result<ConsultaNfeResult>.Failure("NAO_CONFIGURADO", "Consulta nao configurada");
        public Result<StatusServicoResult> StatusServicoResult { get; set; } = Result<StatusServicoResult>.Failure("NAO_CONFIGURADO", "Status nao configurado");
        public Result<CertificadoInfoResult> CertificadoInfoResult { get; set; } = Result<CertificadoInfoResult>.Failure("NAO_CONFIGURADO", "Certificado nao configurado");

        public int ConsultaCalls { get; private set; }
        public int StatusServicoCalls { get; private set; }
        public int CertificadoInfoCalls { get; private set; }

        public Task<Result<ConsultaNfeResult>> ConsultarChaveAsync(string chave, string certBase64, string senha, string ufEmitente, string ambiente)
        {
            ConsultaCalls++;
            return Task.FromResult(ConsultaResult);
        }

        public Task<Result<StatusServicoResult>> ConsultarStatusServicoAsync(string ufEmitente, string ambiente, string certBase64, string senha)
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
