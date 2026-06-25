using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using Nfe.Shared;

namespace Nfe.Core;

public interface INfeSefazService
{
    Task<Result<SefazAutorizacaoResult>> AutorizarAsync(
        string xmlAssinado,
        string uf,
        string ambiente,
        string certBase64,
        string senha,
        string? certPemBase64 = null,
        string? keyPemBase64 = null,
        CancellationToken ct = default);
}

public sealed class SefazAutorizacaoResult
{
    public required string Protocolo { get; init; }
    public required string XmlProcNfe { get; init; }
    public DateTime DataAutorizacao { get; init; }
    public string? ChaveAcesso { get; init; }
    public string? Status { get; init; }
    public string? Motivo { get; init; }
    public string? XmlRetorno { get; init; }
}

public sealed class SefazErroException : Exception
{
    public SefazErroException(string code, string message, string? cStat = null, string? xMotivo = null, string? chaveAcesso = null, string? protocolo = null)
        : base(message)
    {
        Code = code;
        CStat = cStat;
        XMotivo = xMotivo;
        ChaveAcesso = chaveAcesso;
        Protocolo = protocolo;
    }

    public string Code { get; }
    public string? CStat { get; }
    public string? XMotivo { get; }
    public string? ChaveAcesso { get; }
    public string? Protocolo { get; }
}

public sealed class NfeSefazService : INfeSefazService
{
    public async Task<Result<SefazAutorizacaoResult>> AutorizarAsync(
        string xmlAssinado,
        string uf,
        string ambiente,
        string certBase64,
        string senha,
        string? certPemBase64 = null,
        string? keyPemBase64 = null,
        CancellationToken ct = default)
    {
        try
        {
            var url = SefazCodigosHelper.ObterUrlAutorizacao(uf, ambiente);
            var lote = MontarLote(xmlAssinado);
            var soapEnvelope = MontarSoapEnvelope(lote);

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
                "application/soap+xml; charset=utf-8; action=\"http://www.portalfiscal.inf.br/nfe/wsdl/NFeAutorizacao4/nfeAutorizacaoLote\"");

            var response = await httpClient.SendAsync(request, ct);
            var xmlResponse = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                return Result<SefazAutorizacaoResult>.Failure(
                    "SEFAZ_HTTP_ERRO",
                    $"SEFAZ retornou HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). Corpo: {Limitar(xmlResponse, 1000)}");
            }
            return ParsearRetornoAutorizacao(xmlResponse, xmlAssinado);
        }
        catch (HttpRequestException ex)
        {
            return Result<SefazAutorizacaoResult>.Failure("SEFAZ_INDISPONIVEL", $"Erro de comunicação com a SEFAZ: {ex.Message}");
        }
        catch (SefazErroException ex)
        {
            return Result<SefazAutorizacaoResult>.Failure(ex.Code, ex.Message, ex);
        }
        catch (Exception ex)
        {
            return Result<SefazAutorizacaoResult>.Failure("ENVIO_SEFAZ_FALHOU", ex.Message);
        }
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

    private static string Limitar(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static Result<SefazAutorizacaoResult> ParsearRetornoAutorizacao(string xmlRetornoSefaz, string xmlAssinado)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xmlRetornoSefaz);

        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");

        var loteCStat = doc.SelectSingleNode("//nfe:retEnviNFe/nfe:cStat", ns)?.InnerText;
        var loteMotivo = doc.SelectSingleNode("//nfe:retEnviNFe/nfe:xMotivo", ns)?.InnerText ?? "";
        var infProt = doc.SelectSingleNode("//nfe:protNFe/nfe:infProt", ns);
        var cStat = infProt?.SelectSingleNode("nfe:cStat", ns)?.InnerText ?? loteCStat;
        var xMotivo = infProt?.SelectSingleNode("nfe:xMotivo", ns)?.InnerText ?? loteMotivo;
        var protocolo = infProt?.SelectSingleNode("nfe:nProt", ns)?.InnerText ?? "";

        if (cStat == "100")
        {
            var protNFe = doc.SelectSingleNode("//nfe:protNFe", ns);
            if (protNFe == null)
            {
                return Result<SefazAutorizacaoResult>.Failure("SEFAZ_PROTOCOLO_AUSENTE", "Autorização sem grupo protNFe.");
            }

            var xmlProcNfe = NfeProcBuilder.Montar(xmlAssinado, protNFe);

            return Result<SefazAutorizacaoResult>.Success(new SefazAutorizacaoResult
            {
                Protocolo = protocolo,
                XmlProcNfe = xmlProcNfe,
                DataAutorizacao = DateTime.UtcNow,
                ChaveAcesso = infProt?.SelectSingleNode("nfe:chNFe", ns)?.InnerText,
                Status = cStat,
                Motivo = xMotivo,
                XmlRetorno = xmlRetornoSefaz
            });
        }

        if (loteCStat == "104" && infProt == null)
        {
            return Result<SefazAutorizacaoResult>.Failure("SEFAZ_104", "[104] Lote processado sem protocolo da NF-e.");
        }

        var chave = infProt?.SelectSingleNode("nfe:chNFe", ns)?.InnerText;
        var code = $"SEFAZ_{cStat}";
        var exception = new SefazErroException(code, $"[{cStat}] {xMotivo}", cStat, xMotivo, chave, protocolo);
        return Result<SefazAutorizacaoResult>.Failure(exception.Code, exception.Message, exception);
    }

    private static string MontarLote(string xmlNfe)
    {
        var nfeSemDeclaracao = RemoverDeclaracaoXml(xmlNfe);

        var sb = new StringBuilder();
        using var writer = XmlWriter.Create(sb, CriarXmlWriterSettings(omitXmlDeclaration: true));

        writer.WriteStartElement("enviNFe", "http://www.portalfiscal.inf.br/nfe");
        writer.WriteAttributeString("versao", "4.00");
        writer.WriteElementString("idLote", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
        writer.WriteElementString("indSinc", "1");
        writer.WriteRaw(nfeSemDeclaracao);
        writer.WriteEndElement();

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

    private static string MontarSoapEnvelope(string body)
    {
        var sb = new StringBuilder();
        using var writer = XmlWriter.Create(sb, CriarXmlWriterSettings(omitXmlDeclaration: false));

        writer.WriteStartDocument();
        writer.WriteStartElement("soap12", "Envelope", "http://www.w3.org/2003/05/soap-envelope");
        writer.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
        writer.WriteAttributeString("xmlns", "xsd", null, "http://www.w3.org/2001/XMLSchema");
        writer.WriteStartElement("soap12", "Body", "http://www.w3.org/2003/05/soap-envelope");
        writer.WriteStartElement("nfeDadosMsg", "http://www.portalfiscal.inf.br/nfe/wsdl/NFeAutorizacao4");
        writer.WriteRaw(body);
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();

        writer.Flush();
        return sb.ToString();
    }

    private static XmlWriterSettings CriarXmlWriterSettings(bool omitXmlDeclaration) => new()
    {
        Encoding = new UTF8Encoding(false),
        Indent = false,
        OmitXmlDeclaration = omitXmlDeclaration
    };

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
