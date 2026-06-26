using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using Nfe.Shared;

namespace Nfe.Core;

public interface INfeEventoService
{
    Task<Result<NfeEventoResult>> CancelarAsync(
        CancelarNfeRequest request,
        string certBase64,
        string senha,
        string? certPemBase64 = null,
        string? keyPemBase64 = null,
        CancellationToken ct = default);

    Task<Result<NfeEventoResult>> RegistrarCartaCorrecaoAsync(
        CartaCorrecaoRequest request,
        string certBase64,
        string senha,
        string? certPemBase64 = null,
        string? keyPemBase64 = null,
        CancellationToken ct = default);

    Task<Result<NfeInutilizacaoResult>> InutilizarAsync(
        InutilizarNumeracaoRequest request,
        string certBase64,
        string senha,
        string? certPemBase64 = null,
        string? keyPemBase64 = null,
        CancellationToken ct = default);

    Result<string> MontarXmlCancelamento(CancelarNfeRequest request);
    Result<string> MontarXmlCartaCorrecao(CartaCorrecaoRequest request);
    Result<string> MontarXmlInutilizacao(InutilizarNumeracaoRequest request);
}

public sealed record NfeEventoResult
{
    public required string Status { get; init; }
    public required string Motivo { get; init; }
    public required string ChaveAcesso { get; init; }
    public required string TipoEvento { get; init; }
    public required int SequenciaEvento { get; init; }
    public string? Protocolo { get; init; }
    public string? XmlEvento { get; init; }
    public string? XmlRetorno { get; init; }
    public string? XmlProcEventoNfe { get; init; }
}

public sealed record NfeInutilizacaoResult
{
    public required string Status { get; init; }
    public required string Motivo { get; init; }
    public required string Uf { get; init; }
    public required string Ano { get; init; }
    public required string CnpjEmitente { get; init; }
    public required string Serie { get; init; }
    public required long NumeroInicial { get; init; }
    public required long NumeroFinal { get; init; }
    public string? Protocolo { get; init; }
    public string? XmlInutilizacao { get; init; }
    public string? XmlRetorno { get; init; }
}

public sealed class NfeEventoService : INfeEventoService
{
    private const string NsNfe = "http://www.portalfiscal.inf.br/nfe";
    private const string EventoCancelamento = "110111";
    private const string EventoCartaCorrecao = "110110";
    private const string VersaoEvento = "1.00";
    private const string VersaoNfe = "4.00";
    private const string CondicaoUsoCce =
        "A Carta de Correcao e disciplinada pelo paragrafo 1o-A do art. 7o do Convenio S/N, de 15 de dezembro de 1970 e pode ser utilizada para regularizacao de erro ocorrido na emissao de documento fiscal, desde que o erro nao esteja relacionado com: I - as variaveis que determinam o valor do imposto tais como: base de calculo, aliquota, diferenca de preco, quantidade, valor da operacao ou da prestacao; II - a correcao de dados cadastrais que implique mudanca do remetente ou do destinatario; III - a data de emissao ou de saida.";

    private readonly INfeAssinadorService _assinador;

    public NfeEventoService(INfeAssinadorService assinador)
    {
        _assinador = assinador;
    }

    public async Task<Result<NfeEventoResult>> CancelarAsync(
        CancelarNfeRequest request,
        string certBase64,
        string senha,
        string? certPemBase64 = null,
        string? keyPemBase64 = null,
        CancellationToken ct = default)
    {
        var xmlResult = MontarXmlCancelamento(request);
        if (!xmlResult.IsSuccess) return Result<NfeEventoResult>.Failure(xmlResult.ErrorCode!, xmlResult.ErrorMessage!);

        return await AssinarEEnviarEventoAsync(
            xmlResult.Value!,
            request.Uf,
            request.Ambiente,
            request.ChaveAcesso.OnlyAlphaNumericUpper(),
            EventoCancelamento,
            1,
            certBase64,
            senha,
            certPemBase64,
            keyPemBase64,
            ct);
    }

    public async Task<Result<NfeEventoResult>> RegistrarCartaCorrecaoAsync(
        CartaCorrecaoRequest request,
        string certBase64,
        string senha,
        string? certPemBase64 = null,
        string? keyPemBase64 = null,
        CancellationToken ct = default)
    {
        var xmlResult = MontarXmlCartaCorrecao(request);
        if (!xmlResult.IsSuccess) return Result<NfeEventoResult>.Failure(xmlResult.ErrorCode!, xmlResult.ErrorMessage!);

        return await AssinarEEnviarEventoAsync(
            xmlResult.Value!,
            request.Uf,
            request.Ambiente,
            request.ChaveAcesso.OnlyAlphaNumericUpper(),
            EventoCartaCorrecao,
            request.SequenciaEvento,
            certBase64,
            senha,
            certPemBase64,
            keyPemBase64,
            ct);
    }

    public async Task<Result<NfeInutilizacaoResult>> InutilizarAsync(
        InutilizarNumeracaoRequest request,
        string certBase64,
        string senha,
        string? certPemBase64 = null,
        string? keyPemBase64 = null,
        CancellationToken ct = default)
    {
        var xmlResult = MontarXmlInutilizacao(request);
        if (!xmlResult.IsSuccess) return Result<NfeInutilizacaoResult>.Failure(xmlResult.ErrorCode!, xmlResult.ErrorMessage!);

        var assResult = Assinar(xmlResult.Value!, "infInut", certBase64, senha, certPemBase64, keyPemBase64);
        if (!assResult.IsSuccess) return Result<NfeInutilizacaoResult>.Failure(assResult.ErrorCode!, assResult.ErrorMessage!);

        try
        {
            var xmlAssinado = assResult.Value!.XmlAssinado;
            var soap = MontarSoapEnvelope(xmlAssinado, "NFeInutilizacao4");
            var xmlRetorno = await EnviarSoapAsync(
                SefazCodigosHelper.ObterUrlInutilizacao(request.Uf, request.Ambiente),
                soap,
                "http://www.portalfiscal.inf.br/nfe/wsdl/NFeInutilizacao4/nfeInutilizacaoNF",
                certBase64,
                senha,
                certPemBase64,
                keyPemBase64,
                ct);

            return ParsearRetornoInutilizacao(xmlRetorno, xmlAssinado);
        }
        catch (HttpRequestException ex)
        {
            return Result<NfeInutilizacaoResult>.Failure("SEFAZ_INDISPONIVEL", $"Erro de comunicação com a SEFAZ: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Result<NfeInutilizacaoResult>.Failure("INUTILIZACAO_FALHOU", ex.Message);
        }
    }

    public Result<string> MontarXmlCancelamento(CancelarNfeRequest request)
    {
        var validation = ValidarEventoBase(request.Ambiente, request.Uf, request.ChaveAcesso, request.CnpjEmitente);
        if (!validation.IsSuccess) return validation;

        var justificativa = NormalizarTexto(request.Justificativa);
        if (justificativa.Length is < 15 or > 255)
        {
            return Result<string>.Failure("JUSTIFICATIVA_INVALIDA", "A justificativa deve ter entre 15 e 255 caracteres.");
        }

        var protocolo = request.ProtocoloAutorizacao.OnlyAlphaNumericUpper();
        if (string.IsNullOrWhiteSpace(protocolo))
        {
            return Result<string>.Failure("PROTOCOLO_INVALIDO", "Informe o protocolo de autorização da NF-e.");
        }

        return Result<string>.Success(MontarXmlEvento(
            request.Ambiente,
            request.Uf,
            request.CnpjEmitente,
            request.ChaveAcesso,
            EventoCancelamento,
            1,
            writer =>
            {
                writer.WriteElementString("descEvento", "Cancelamento");
                writer.WriteElementString("nProt", protocolo);
                writer.WriteElementString("xJust", justificativa);
            }));
    }

    public Result<string> MontarXmlCartaCorrecao(CartaCorrecaoRequest request)
    {
        var validation = ValidarEventoBase(request.Ambiente, request.Uf, request.ChaveAcesso, request.CnpjEmitente);
        if (!validation.IsSuccess) return validation;

        if (request.SequenciaEvento is < 1 or > 20)
        {
            return Result<string>.Failure("SEQUENCIA_EVENTO_INVALIDA", "A sequência da CC-e deve ficar entre 1 e 20.");
        }

        var correcao = NormalizarTexto(request.Correcao);
        if (correcao.Length is < 15 or > 1000)
        {
            return Result<string>.Failure("CORRECAO_INVALIDA", "A correção deve ter entre 15 e 1000 caracteres.");
        }

        return Result<string>.Success(MontarXmlEvento(
            request.Ambiente,
            request.Uf,
            request.CnpjEmitente,
            request.ChaveAcesso,
            EventoCartaCorrecao,
            request.SequenciaEvento,
            writer =>
            {
                writer.WriteElementString("descEvento", "Carta de Correcao");
                writer.WriteElementString("xCorrecao", correcao);
                writer.WriteElementString("xCondUso", CondicaoUsoCce);
            }));
    }

    public Result<string> MontarXmlInutilizacao(InutilizarNumeracaoRequest request)
    {
        var ambiente = request.Ambiente.Trim();
        if (ambiente is not ("1" or "2"))
        {
            return Result<string>.Failure("AMBIENTE_INVALIDO", "Ambiente deve ser 1 (produção) ou 2 (homologação).");
        }

        var uf = request.Uf.Trim().ToUpperInvariant();
        int codigoUf;
        try
        {
            codigoUf = SefazCodigosHelper.ObterCodigoUf(uf);
        }
        catch (ArgumentException ex)
        {
            return Result<string>.Failure("UF_INVALIDA", ex.Message);
        }

        var cUf = codigoUf.ToString(CultureInfo.InvariantCulture);
        var cnpj = request.CnpjEmitente.OnlyAlphaNumericUpper();
        var ano = request.Ano.OnlyDigits();
        var modelo = request.Modelo.OnlyDigits();
        var serie = request.Serie.OnlyDigits();
        var justificativa = NormalizarTexto(request.Justificativa);

        if (cnpj.Length != 14) return Result<string>.Failure("CNPJ_INVALIDO", "CNPJ do emitente deve ter 14 caracteres.");
        if (ano.Length == 4) ano = ano[2..];
        if (ano.Length != 2) return Result<string>.Failure("ANO_INVALIDO", "Ano deve estar no formato AA ou AAAA.");
        if (modelo != "55") return Result<string>.Failure("MODELO_INVALIDO", "Apenas NF-e modelo 55 é suportada.");
        if (string.IsNullOrWhiteSpace(serie) || serie.Length > 3) return Result<string>.Failure("SERIE_INVALIDA", "Série deve ter até 3 dígitos.");
        if (request.NumeroInicial <= 0 || request.NumeroFinal <= 0 || request.NumeroInicial > request.NumeroFinal)
        {
            return Result<string>.Failure("NUMERACAO_INVALIDA", "Informe uma faixa numérica válida.");
        }
        if (justificativa.Length is < 15 or > 255)
        {
            return Result<string>.Failure("JUSTIFICATIVA_INVALIDA", "A justificativa deve ter entre 15 e 255 caracteres.");
        }

        var id = "ID" +
                 cUf +
                 ano +
                 cnpj +
                 modelo.PadLeft(2, '0') +
                 serie.PadLeft(3, '0') +
                 request.NumeroInicial.ToString("D9", CultureInfo.InvariantCulture) +
                 request.NumeroFinal.ToString("D9", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        using var writer = XmlWriter.Create(sb, CriarXmlWriterSettings(omitXmlDeclaration: false));

        writer.WriteStartDocument();
        writer.WriteStartElement("inutNFe", NsNfe);
        writer.WriteAttributeString("versao", VersaoNfe);
        writer.WriteStartElement("infInut");
        writer.WriteAttributeString("Id", id);
        writer.WriteElementString("tpAmb", ambiente);
        writer.WriteElementString("xServ", "INUTILIZAR");
        writer.WriteElementString("cUF", cUf);
        writer.WriteElementString("ano", ano);
        writer.WriteElementString("CNPJ", cnpj);
        writer.WriteElementString("mod", modelo.PadLeft(2, '0'));
        writer.WriteElementString("serie", long.Parse(serie, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
        writer.WriteElementString("nNFIni", request.NumeroInicial.ToString(CultureInfo.InvariantCulture));
        writer.WriteElementString("nNFFin", request.NumeroFinal.ToString(CultureInfo.InvariantCulture));
        writer.WriteElementString("xJust", justificativa);
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
        writer.Flush();

        return Result<string>.Success(sb.ToString());
    }

    private async Task<Result<NfeEventoResult>> AssinarEEnviarEventoAsync(
        string xmlEvento,
        string uf,
        string ambiente,
        string chaveAcesso,
        string tipoEvento,
        int sequenciaEvento,
        string certBase64,
        string senha,
        string? certPemBase64,
        string? keyPemBase64,
        CancellationToken ct)
    {
        var assResult = Assinar(xmlEvento, "infEvento", certBase64, senha, certPemBase64, keyPemBase64);
        if (!assResult.IsSuccess) return Result<NfeEventoResult>.Failure(assResult.ErrorCode!, assResult.ErrorMessage!);

        try
        {
            var xmlAssinado = assResult.Value!.XmlAssinado;
            var envEvento = MontarEnvEvento(xmlAssinado);
            var soap = MontarSoapEnvelope(envEvento, "NFeRecepcaoEvento4");
            var xmlRetorno = await EnviarSoapAsync(
                SefazCodigosHelper.ObterUrlRecepcaoEvento(uf, ambiente),
                soap,
                "http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4/nfeRecepcaoEvento",
                certBase64,
                senha,
                certPemBase64,
                keyPemBase64,
                ct);

            return ParsearRetornoEvento(xmlRetorno, xmlAssinado, chaveAcesso, tipoEvento, sequenciaEvento);
        }
        catch (HttpRequestException ex)
        {
            return Result<NfeEventoResult>.Failure("SEFAZ_INDISPONIVEL", $"Erro de comunicação com a SEFAZ: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Result<NfeEventoResult>.Failure("EVENTO_FALHOU", ex.Message);
        }
    }

    private Result<AssinaturaResult> Assinar(
        string xml,
        string elementoAssinavel,
        string certBase64,
        string senha,
        string? certPemBase64,
        string? keyPemBase64)
    {
        var usandoPem = !string.IsNullOrWhiteSpace(certPemBase64) && !string.IsNullOrWhiteSpace(keyPemBase64);
        return usandoPem
            ? _assinador.AssinarElementoComPemConteudo(
                xml,
                elementoAssinavel,
                "ID",
                Encoding.UTF8.GetString(Convert.FromBase64String(certPemBase64!)),
                Encoding.UTF8.GetString(Convert.FromBase64String(keyPemBase64!)))
            : _assinador.AssinarElemento(xml, elementoAssinavel, "ID", certBase64, senha);
    }

    private static string MontarXmlEvento(
        string ambiente,
        string uf,
        string cnpjEmitente,
        string chaveAcesso,
        string tipoEvento,
        int sequenciaEvento,
        Action<XmlWriter> escreverDetalhe)
    {
        var ufNormalizada = uf.Trim().ToUpperInvariant();
        var cUf = SefazCodigosHelper.ObterCodigoUf(ufNormalizada).ToString(CultureInfo.InvariantCulture);
        var cnpj = cnpjEmitente.OnlyAlphaNumericUpper();
        var chave = chaveAcesso.OnlyAlphaNumericUpper();
        var id = $"ID{tipoEvento}{chave}{sequenciaEvento:D2}";

        var sb = new StringBuilder();
        using var writer = XmlWriter.Create(sb, CriarXmlWriterSettings(omitXmlDeclaration: false));

        writer.WriteStartDocument();
        writer.WriteStartElement("evento", NsNfe);
        writer.WriteAttributeString("versao", VersaoEvento);
        writer.WriteStartElement("infEvento");
        writer.WriteAttributeString("Id", id);
        writer.WriteElementString("cOrgao", cUf);
        writer.WriteElementString("tpAmb", ambiente.Trim());
        writer.WriteElementString("CNPJ", cnpj);
        writer.WriteElementString("chNFe", chave);
        writer.WriteElementString("dhEvento", DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture));
        writer.WriteElementString("tpEvento", tipoEvento);
        writer.WriteElementString("nSeqEvento", sequenciaEvento.ToString(CultureInfo.InvariantCulture));
        writer.WriteElementString("verEvento", VersaoEvento);
        writer.WriteStartElement("detEvento");
        writer.WriteAttributeString("versao", VersaoEvento);
        escreverDetalhe(writer);
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
        writer.Flush();

        return sb.ToString();
    }

    private static string MontarEnvEvento(string xmlEventoAssinado)
    {
        var sb = new StringBuilder();
        using var writer = XmlWriter.Create(sb, CriarXmlWriterSettings(omitXmlDeclaration: true));

        writer.WriteStartElement("envEvento", NsNfe);
        writer.WriteAttributeString("versao", VersaoEvento);
        writer.WriteElementString("idLote", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        writer.WriteRaw(RemoverDeclaracaoXml(xmlEventoAssinado));
        writer.WriteEndElement();
        writer.Flush();
        return sb.ToString();
    }

    private static async Task<string> EnviarSoapAsync(
        string url,
        string soapEnvelope,
        string soapAction,
        string certBase64,
        string senha,
        string? certPemBase64,
        string? keyPemBase64,
        CancellationToken ct)
    {
        using var certificadoCliente = CarregarCertificadoCliente(certBase64, senha, certPemBase64, keyPemBase64);
        using var handler = new HttpClientHandler
        {
            ClientCertificateOptions = ClientCertificateOption.Manual
        };
        handler.ClientCertificates.Add(certificadoCliente);

        using var httpClient = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(soapEnvelope, Encoding.UTF8);
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(
            $"application/soap+xml; charset=utf-8; action=\"{soapAction}\"");

        var response = await httpClient.SendAsync(request, ct);
        var xmlResponse = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"SEFAZ retornou HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). Corpo: {Limitar(xmlResponse, 1000)}");
        }

        return xmlResponse;
    }

    private static Result<NfeEventoResult> ParsearRetornoEvento(
        string xmlRetornoSefaz,
        string xmlEventoAssinado,
        string chaveAcesso,
        string tipoEvento,
        int sequenciaEvento)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xmlRetornoSefaz);

        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("nfe", NsNfe);

        var infEvento = doc.SelectSingleNode("//nfe:retEvento/nfe:infEvento", ns);
        var cStat = infEvento?.SelectSingleNode("nfe:cStat", ns)?.InnerText
            ?? doc.SelectSingleNode("//nfe:retEnvEvento/nfe:cStat", ns)?.InnerText
            ?? "";
        var xMotivo = infEvento?.SelectSingleNode("nfe:xMotivo", ns)?.InnerText
            ?? doc.SelectSingleNode("//nfe:retEnvEvento/nfe:xMotivo", ns)?.InnerText
            ?? "";
        var protocolo = infEvento?.SelectSingleNode("nfe:nProt", ns)?.InnerText;

        var result = new NfeEventoResult
        {
            Status = cStat,
            Motivo = xMotivo,
            ChaveAcesso = infEvento?.SelectSingleNode("nfe:chNFe", ns)?.InnerText ?? chaveAcesso,
            TipoEvento = infEvento?.SelectSingleNode("nfe:tpEvento", ns)?.InnerText ?? tipoEvento,
            SequenciaEvento = int.TryParse(infEvento?.SelectSingleNode("nfe:nSeqEvento", ns)?.InnerText, out var seq) ? seq : sequenciaEvento,
            Protocolo = protocolo,
            XmlEvento = xmlEventoAssinado,
            XmlRetorno = xmlRetornoSefaz,
            XmlProcEventoNfe = infEvento == null ? null : MontarProcEvento(xmlEventoAssinado, infEvento.ParentNode!)
        };

        return cStat is "135" or "136" or "155"
            ? Result<NfeEventoResult>.Success(result)
            : Result<NfeEventoResult>.Failure($"SEFAZ_{cStat}", $"[{cStat}] {xMotivo}");
    }

    private static Result<NfeInutilizacaoResult> ParsearRetornoInutilizacao(string xmlRetornoSefaz, string xmlInutilizacaoAssinado)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xmlRetornoSefaz);

        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("nfe", NsNfe);

        var infInut = doc.SelectSingleNode("//nfe:retInutNFe/nfe:infInut", ns);
        var cStat = infInut?.SelectSingleNode("nfe:cStat", ns)?.InnerText ?? "";
        var xMotivo = infInut?.SelectSingleNode("nfe:xMotivo", ns)?.InnerText ?? "";

        var result = new NfeInutilizacaoResult
        {
            Status = cStat,
            Motivo = xMotivo,
            Uf = infInut?.SelectSingleNode("nfe:cUF", ns)?.InnerText ?? "",
            Ano = infInut?.SelectSingleNode("nfe:ano", ns)?.InnerText ?? "",
            CnpjEmitente = infInut?.SelectSingleNode("nfe:CNPJ", ns)?.InnerText ?? "",
            Serie = infInut?.SelectSingleNode("nfe:serie", ns)?.InnerText ?? "",
            NumeroInicial = long.TryParse(infInut?.SelectSingleNode("nfe:nNFIni", ns)?.InnerText, out var ini) ? ini : 0,
            NumeroFinal = long.TryParse(infInut?.SelectSingleNode("nfe:nNFFin", ns)?.InnerText, out var fim) ? fim : 0,
            Protocolo = infInut?.SelectSingleNode("nfe:nProt", ns)?.InnerText,
            XmlInutilizacao = xmlInutilizacaoAssinado,
            XmlRetorno = xmlRetornoSefaz
        };

        return cStat == "102"
            ? Result<NfeInutilizacaoResult>.Success(result)
            : Result<NfeInutilizacaoResult>.Failure($"SEFAZ_{cStat}", $"[{cStat}] {xMotivo}");
    }

    private static string MontarProcEvento(string xmlEventoAssinado, XmlNode retEvento)
    {
        var procDoc = new XmlDocument { PreserveWhitespace = true };
        var eventoDoc = new XmlDocument { PreserveWhitespace = true };
        eventoDoc.LoadXml(xmlEventoAssinado);

        var procEvento = procDoc.CreateElement("procEventoNFe", NsNfe);
        procEvento.SetAttribute("versao", VersaoEvento);
        procDoc.AppendChild(procEvento);

        procEvento.AppendChild(procDoc.ImportNode(eventoDoc.DocumentElement!, true));
        procEvento.AppendChild(procDoc.ImportNode(retEvento, true));

        return procDoc.OuterXml;
    }

    private static Result<string> ValidarEventoBase(string ambiente, string uf, string chaveAcesso, string cnpjEmitente)
    {
        if (ambiente.Trim() is not ("1" or "2"))
        {
            return Result<string>.Failure("AMBIENTE_INVALIDO", "Ambiente deve ser 1 (produção) ou 2 (homologação).");
        }

        try
        {
            _ = SefazCodigosHelper.ObterCodigoUf(uf.Trim().ToUpperInvariant());
        }
        catch (ArgumentException ex)
        {
            return Result<string>.Failure("UF_INVALIDA", ex.Message);
        }

        if (chaveAcesso.OnlyAlphaNumericUpper().Length != 44)
        {
            return Result<string>.Failure("CHAVE_INVALIDA", "Chave de acesso deve ter 44 caracteres.");
        }

        if (cnpjEmitente.OnlyAlphaNumericUpper().Length != 14)
        {
            return Result<string>.Failure("CNPJ_INVALIDO", "CNPJ do emitente deve ter 14 caracteres.");
        }

        return Result<string>.Success("");
    }

    private static X509Certificate2 CarregarCertificadoCliente(
        string certBase64,
        string senha,
        string? certPemBase64,
        string? keyPemBase64)
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
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable,
            new Pkcs12LoaderLimits());
    }

    private static string MontarSoapEnvelope(string body, string wsdlService)
    {
        var sb = new StringBuilder();
        using var writer = XmlWriter.Create(sb, CriarXmlWriterSettings(omitXmlDeclaration: false));

        writer.WriteStartDocument();
        writer.WriteStartElement("soap12", "Envelope", "http://www.w3.org/2003/05/soap-envelope");
        writer.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
        writer.WriteAttributeString("xmlns", "xsd", null, "http://www.w3.org/2001/XMLSchema");
        writer.WriteStartElement("soap12", "Body", "http://www.w3.org/2003/05/soap-envelope");
        writer.WriteStartElement("nfeDadosMsg", $"http://www.portalfiscal.inf.br/nfe/wsdl/{wsdlService}");
        writer.WriteRaw(RemoverDeclaracaoXml(body));
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();

        writer.Flush();
        return sb.ToString();
    }

    private static string RemoverDeclaracaoXml(string xml)
    {
        var semEspacosIniciais = xml.TrimStart();
        if (!semEspacosIniciais.StartsWith("<?xml", StringComparison.Ordinal))
        {
            return xml;
        }

        var fimDeclaracao = semEspacosIniciais.IndexOf("?>", StringComparison.Ordinal);
        if (fimDeclaracao < 0)
        {
            return xml;
        }

        return semEspacosIniciais[(fimDeclaracao + 2)..].TrimStart();
    }

    private static string NormalizarTexto(string value)
        => string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Limitar(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static XmlWriterSettings CriarXmlWriterSettings(bool omitXmlDeclaration) => new()
    {
        Encoding = new UTF8Encoding(false),
        Indent = false,
        OmitXmlDeclaration = omitXmlDeclaration
    };
}
