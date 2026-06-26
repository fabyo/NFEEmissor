using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using Nfe.Shared;

namespace Nfe.Core;

public interface INfeAssinadorService
{
    Result<AssinaturaResult> Assinar(string xmlNfe, string certBase64, string senha);
    Result<AssinaturaResult> AssinarComPem(string xmlNfe, string certPemPath, string keyPemPath);
    /// <summary>Assina usando o conteudo PEM como string (nao caminho). Ideal para header Base64.</summary>
    Result<AssinaturaResult> AssinarComPemConteudo(string xmlNfe, string certPemConteudo, string keyPemConteudo);
    Result<AssinaturaResult> AssinarElemento(string xml, string elementoAssinavel, string idPrefixo, string certBase64, string senha);
    Result<AssinaturaResult> AssinarElementoComPemConteudo(string xml, string elementoAssinavel, string idPrefixo, string certPemConteudo, string keyPemConteudo);
}

public sealed class AssinaturaResult
{
    public required string ChaveAcesso { get; init; }
    public required string XmlAssinado { get; init; }
}

public sealed class NfeAssinadorService : INfeAssinadorService
{
    public Result<AssinaturaResult> Assinar(string xmlNfe, string certBase64, string senha)
    {
        try
        {
            var certBytes = Convert.FromBase64String(certBase64);
            using var cert = X509CertificateLoader.LoadPkcs12(
                certBytes,
                senha,
                X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable,
                new Pkcs12LoaderLimits());

            return AssinarComCertificado(xmlNfe, cert);
        }
        catch (Exception ex)
        {
            return Result<AssinaturaResult>.Failure("ASSINATURA_FALHOU", ex.Message);
        }
    }

    public Result<AssinaturaResult> AssinarComPem(string xmlNfe, string certPemPath, string keyPemPath)
    {
        try
        {
            if (!File.Exists(certPemPath)) return Result<AssinaturaResult>.Failure("CERTIFICADO_NAO_ENCONTRADO", $"Arquivo PEM do certificado não encontrado: {certPemPath}");
            if (!File.Exists(keyPemPath)) return Result<AssinaturaResult>.Failure("CHAVE_NAO_ENCONTRADO", $"Arquivo PEM da chave privada não encontrado: {keyPemPath}");

            using var cert = CarregarCertificadoPem(certPemPath, keyPemPath);

            return AssinarComCertificado(xmlNfe, cert);
        }
        catch (Exception ex)
        {
            return Result<AssinaturaResult>.Failure("ASSINATURA_PEM_FALHOU", ex.Message);
        }
    }

    public Result<AssinaturaResult> AssinarComPemConteudo(string xmlNfe, string certPemConteudo, string keyPemConteudo)
    {
        try
        {
            using var cert = CarregarCertificadoPem(certPemConteudo, keyPemConteudo);

            return AssinarComCertificado(xmlNfe, cert);
        }
        catch (Exception ex)
        {
            return Result<AssinaturaResult>.Failure("ASSINATURA_PEM_FALHOU", ex.Message);
        }
    }

    public Result<AssinaturaResult> AssinarElemento(
        string xml,
        string elementoAssinavel,
        string idPrefixo,
        string certBase64,
        string senha)
    {
        try
        {
            var certBytes = Convert.FromBase64String(certBase64);
            using var cert = X509CertificateLoader.LoadPkcs12(
                certBytes,
                senha,
                X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable,
                new Pkcs12LoaderLimits());

            return AssinarElementoComCertificado(xml, elementoAssinavel, idPrefixo, cert);
        }
        catch (Exception ex)
        {
            return Result<AssinaturaResult>.Failure("ASSINATURA_FALHOU", ex.Message);
        }
    }

    public Result<AssinaturaResult> AssinarElementoComPemConteudo(
        string xml,
        string elementoAssinavel,
        string idPrefixo,
        string certPemConteudo,
        string keyPemConteudo)
    {
        try
        {
            using var cert = CarregarCertificadoPem(certPemConteudo, keyPemConteudo);
            return AssinarElementoComCertificado(xml, elementoAssinavel, idPrefixo, cert);
        }
        catch (Exception ex)
        {
            return Result<AssinaturaResult>.Failure("ASSINATURA_PEM_FALHOU", ex.Message);
        }
    }

    private static X509Certificate2 CarregarCertificadoPem(string certPem, string keyPem)
    {
        var certBytes = File.Exists(certPem)
            ? File.ReadAllBytes(certPem)
            : System.Text.Encoding.UTF8.GetBytes(certPem);

        using var publicCert = X509CertificateLoader.LoadCertificate(certBytes);
        using var rsa = RSA.Create();

        if (File.Exists(keyPem))
        {
            rsa.ImportFromPem(File.ReadAllText(keyPem));
        }
        else
        {
            rsa.ImportFromPem(keyPem);
        }

        return publicCert.CopyWithPrivateKey(rsa);
    }

    private static Result<AssinaturaResult> AssinarComCertificado(string xmlNfe, X509Certificate2 cert)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xmlNfe);

        var chave = ExtrairChaveAcesso(doc);

        var rsa = cert.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("Certificado sem chave privada RSA");

        var referenceUri = $"#NFe{chave}";

        // Criamos uma classe herdada customizada de SignedXml para encontrar a tag 'Id' sem SetIdAttributeNode
        var signedXml = new NfeSignedXml(doc) { SigningKey = rsa };
        var signedInfo = signedXml.SignedInfo ?? throw new InvalidOperationException("SignedInfo nao foi inicializado.");
        signedInfo.SignatureMethod = "http://www.w3.org/2000/09/xmldsig#rsa-sha1";
        signedInfo.CanonicalizationMethod = "http://www.w3.org/TR/2001/REC-xml-c14n-20010315";

        var reference = new Reference(referenceUri);
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigC14NTransform());
        reference.DigestMethod = "http://www.w3.org/2000/09/xmldsig#sha1";
        signedXml.AddReference(reference);

        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(cert));
        signedXml.KeyInfo = keyInfo;

        signedXml.ComputeSignature();

        var xmlSignature = signedXml.GetXml();
        var infNfe = doc.GetElementsByTagName("infNFe")[0]
            ?? throw new InvalidOperationException("Elemento infNFe não encontrado");

        infNfe.ParentNode!.AppendChild(doc.ImportNode(xmlSignature, true));

        return Result<AssinaturaResult>.Success(new AssinaturaResult
        {
            ChaveAcesso = chave,
            XmlAssinado = doc.OuterXml
        });
    }

    private static Result<AssinaturaResult> AssinarElementoComCertificado(
        string xml,
        string elementoAssinavel,
        string idPrefixo,
        X509Certificate2 cert)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);

        var element = doc.GetElementsByTagName(elementoAssinavel)[0] as XmlElement
            ?? throw new InvalidOperationException($"Elemento {elementoAssinavel} não encontrado");
        var id = element.GetAttribute("Id");
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException($"Elemento {elementoAssinavel} sem atributo Id");
        }

        var rsa = cert.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("Certificado sem chave privada RSA");

        var signedXml = new NfeSignedXml(doc) { SigningKey = rsa };
        var signedInfo = signedXml.SignedInfo ?? throw new InvalidOperationException("SignedInfo nao foi inicializado.");
        signedInfo.SignatureMethod = "http://www.w3.org/2000/09/xmldsig#rsa-sha1";
        signedInfo.CanonicalizationMethod = "http://www.w3.org/TR/2001/REC-xml-c14n-20010315";

        var reference = new Reference($"#{id}");
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigC14NTransform());
        reference.DigestMethod = "http://www.w3.org/2000/09/xmldsig#sha1";
        signedXml.AddReference(reference);

        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(cert));
        signedXml.KeyInfo = keyInfo;

        signedXml.ComputeSignature();

        var xmlSignature = signedXml.GetXml();
        element.ParentNode!.AppendChild(doc.ImportNode(xmlSignature, true));

        return Result<AssinaturaResult>.Success(new AssinaturaResult
        {
            ChaveAcesso = id.StartsWith(idPrefixo, StringComparison.Ordinal) ? id[idPrefixo.Length..] : id,
            XmlAssinado = doc.OuterXml
        });
    }

    private static string ExtrairChaveAcesso(XmlDocument doc)
    {
        var infNfe = doc.GetElementsByTagName("infNFe")[0]
            ?? throw new InvalidOperationException("infNFe não encontrado");
        return infNfe.Attributes!["Id"]!.Value.Replace("NFe", "");
    }
}

/// <summary>
/// Sobrescreve o comportamento padrão do SignedXml para mapear corretamente o elemento com o atributo Id.
/// </summary>
internal sealed class NfeSignedXml : SignedXml
{
    public NfeSignedXml(XmlDocument xml) : base(xml) { }

    public override XmlElement? GetIdElement(XmlDocument? doc, string id)
    {
        if (doc == null) return null;

        // Tenta o comportamento padrão
        var elem = base.GetIdElement(doc, id);
        if (elem != null) return elem;

        // Fallback: busca manual por qualquer elemento com atributo Id correspondente.
        var list = doc.GetElementsByTagName("*");
        foreach (XmlNode node in list)
        {
            if (node is XmlElement el && el.GetAttribute("Id") == id)
            {
                return el;
            }
        }

        return null;
    }
}
