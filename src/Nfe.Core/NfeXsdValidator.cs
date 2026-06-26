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
    private readonly string _tiposBasicosFileName;
    private readonly string _nfeFileName;

    public NfeXsdValidator(
        string schemasPath = "schemas/v4",
        string? tiposBasicosFileName = null,
        string? nfeFileName = null)
    {
        _schemasPath = schemasPath;
        _tiposBasicosFileName = tiposBasicosFileName ?? DescobrirSchema(_schemasPath, "tiposBasico*.xsd", "tiposBasico");
        _nfeFileName = nfeFileName ?? DescobrirSchema(_schemasPath, "nfe*.xsd", "nfe_v");
        _schemaSet = new XmlSchemaSet();
        CarregarSchemas();
    }

    public string SchemasPath => _schemasPath;
    public string TiposBasicosFileName => _tiposBasicosFileName;
    public string NfeFileName => _nfeFileName;

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
        var tiposBasicosPath = Path.Combine(_schemasPath, _tiposBasicosFileName);
        var nfePath = Path.Combine(_schemasPath, _nfeFileName);

        if (!File.Exists(tiposBasicosPath) || !File.Exists(nfePath))
        {
            throw new DirectoryNotFoundException(
                $"Schemas XSD oficiais não encontrados em {_schemasPath}. " +
                $"Esperado: {_tiposBasicosFileName} e {_nfeFileName}. Execute NFeSchemaManager para sincronizar.");
        }

        _schemaSet.XmlResolver = new XmlUrlResolver();
        _schemaSet.Add("http://www.portalfiscal.inf.br/nfe", XmlReader.Create(nfePath));
        _schemaSet.Compile();
    }

    private static string DescobrirSchema(string schemasPath, string searchPattern, string preferredPrefix)
    {
        if (!Directory.Exists(schemasPath))
        {
            return searchPattern.Replace("*", "_v4.00");
        }

        var files = Directory
            .EnumerateFiles(schemasPath, searchPattern, SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Where(name => name!.StartsWith(preferredPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return files.FirstOrDefault() ?? searchPattern.Replace("*", "_v4.00");
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
