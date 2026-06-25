using System.Text;
using System.Xml;

namespace Nfe.Core;

public static class NfeProcBuilder
{
    public static string Montar(string xmlAssinado, string protNFeXml)
    {
        var protDoc = new XmlDocument { PreserveWhitespace = true };
        protDoc.LoadXml(protNFeXml);

        var protNode = protDoc.DocumentElement?.LocalName == "protNFe"
            ? protDoc.DocumentElement
            : protDoc.GetElementsByTagName("protNFe", "http://www.portalfiscal.inf.br/nfe").OfType<XmlElement>().FirstOrDefault();

        if (protNode == null)
            throw new InvalidOperationException("XML de protocolo não contém o elemento protNFe.");

        return Montar(xmlAssinado, protNode);
    }

    public static string Montar(string xmlAssinado, XmlNode protNFe)
    {
        var nfeDoc = new XmlDocument { PreserveWhitespace = true };
        nfeDoc.LoadXml(xmlAssinado);

        var nfeNode = nfeDoc.DocumentElement?.LocalName == "NFe"
            ? nfeDoc.DocumentElement
            : nfeDoc.GetElementsByTagName("NFe", "http://www.portalfiscal.inf.br/nfe").OfType<XmlElement>().FirstOrDefault();

        if (nfeNode == null)
            throw new InvalidOperationException("XML assinado não contém o elemento NFe.");

        var procDoc = new XmlDocument { PreserveWhitespace = true };
        var declaration = procDoc.CreateXmlDeclaration("1.0", "utf-8", null);
        procDoc.AppendChild(declaration);

        var nfeProc = procDoc.CreateElement("nfeProc", "http://www.portalfiscal.inf.br/nfe");
        nfeProc.SetAttribute("versao", "4.00");
        procDoc.AppendChild(nfeProc);

        nfeProc.AppendChild(procDoc.ImportNode(nfeNode, deep: true));
        nfeProc.AppendChild(procDoc.ImportNode(protNFe, deep: true));

        using var stringWriter = new Utf8StringWriter();
        using var writer = XmlWriter.Create(stringWriter, CriarXmlWriterSettings(omitXmlDeclaration: false));
        procDoc.Save(writer);
        writer.Flush();

        return stringWriter.ToString();
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
