using System.Xml;
using System.Xml.Schema;
using Nfe.Shared;

namespace Nfe.Core;

/// <summary>
/// Valida o XML NF-e gerado contra os schemas XSD oficiais da SEFAZ
/// utilizando os arquivos locais gerenciados pelo NFeSchemaDownloader.
/// </summary>
public sealed class NfeXsdValidator
{
    private readonly XmlSchemaSet _schemaSet;
    private readonly string _schemasPath;

    public NfeXsdValidator(string schemasPath = "schemas/v4")
    {
        _schemasPath = schemasPath;
        _schemaSet = new XmlSchemaSet();
        CarregarSchemas();
    }

    /// <returns>Lista de erros de validação; vazia se o XML é válido.</returns>
    public IReadOnlyList<string> Validar(string xmlNfe)
    {
        var erros = new List<string>();

        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = _schemaSet,
            ValidationFlags =
                XmlSchemaValidationFlags.ProcessIdentityConstraints |
                XmlSchemaValidationFlags.ReportValidationWarnings,
        };

        settings.ValidationEventHandler += (_, args) =>
        {
            if (args.Severity == XmlSeverityType.Error)
                erros.Add($"[XSD] {args.Message} (linha {args.Exception?.LineNumber})");
        };

        try
        {
            using var reader = XmlReader.Create(new StringReader(xmlNfe), settings);
            while (reader.Read()) { }
        }
        catch (XmlException ex)
        {
            erros.Add($"[XML malformado] {ex.Message}");
        }

        return erros;
    }

    private void CarregarSchemas()
    {
        var tiposBasicosPath = Path.Combine(_schemasPath, "tiposBasico_v4.00.xsd");
        var nfePath = Path.Combine(_schemasPath, "nfe_v4.00.xsd");

        if (!File.Exists(tiposBasicosPath) || !File.Exists(nfePath))
        {
            throw new DirectoryNotFoundException($"Schemas XSD oficiais não encontrados em {_schemasPath}. Execute NFeSchemaManager para sincronizar.");
        }

        using var tiposStream = File.OpenRead(tiposBasicosPath);
        using var nfeStream = File.OpenRead(nfePath);

        _schemaSet.Add("http://www.portalfiscal.inf.br/nfe", XmlReader.Create(tiposStream));
        _schemaSet.Add("http://www.portalfiscal.inf.br/nfe", XmlReader.Create(nfeStream));
        _schemaSet.Compile();
    }
}

public static class NfeXsdValidatorExtensions
{
    public static Result<string> ValidarXsd(this string xmlNfe, NfeXsdValidator validator)
    {
        var erros = validator.Validar(xmlNfe);

        return erros.Count == 0
            ? Result<string>.Success(xmlNfe)
            : Result<string>.Failure("XSD_INVALIDO", string.Join(" | ", erros));
    }
}