using System.Text.Json;
using NFEDanfe;
using Nfe.Core;
using Nfe.Shared;
using QuestPDF.Infrastructure;

ConfigurarLicencaQuestPdf();

if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
{
    MostrarAjuda();
    return 0;
}

if (args[0] == "emitir")
{
    var jsonPath = ObterArgumento(args, "--json");
    var certPath = ObterArgumento(args, "--cert"); // Pode ser .pfx ou cert.pem
    var keyPath = ObterArgumento(args, "--key");    // Chave privada caso use PEM (.key ou key.pem)
    var senha = ObterArgumento(args, "--senha") ?? "";
    var outputDir = ObterArgumento(args, "--output-dir") ?? "out";

    if (string.IsNullOrEmpty(jsonPath))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Erro: Parâmetro --json é obrigatório.");
        Console.ResetColor();
        return 1;
    }

    if (string.IsNullOrEmpty(certPath))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Erro: Parâmetro --cert é obrigatório (caminho para arquivo PFX ou PEM).");
        Console.ResetColor();
        return 1;
    }

    try
    {
        if (!File.Exists(jsonPath))
        {
            Console.WriteLine($"Erro: Arquivo JSON não encontrado em: {jsonPath}");
            return 1;
        }

        if (!File.Exists(certPath))
        {
            Console.WriteLine($"Erro: Certificado digital não encontrado em: {certPath}");
            return 1;
        }

        var jsonContent = await File.ReadAllTextAsync(jsonPath);
        var request = JsonSerializer.Deserialize<EmitirNfeRequest>(jsonContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (request == null)
        {
            Console.WriteLine("Erro: Não foi possível desserializar o JSON de entrada.");
            return 1;
        }

        var xmlBuilder = new NfeXmlBuilder();
        var assinador = new NfeAssinadorService();

        Console.WriteLine("1. Gerando XML da NF-e...");
        var buildResult = await xmlBuilder.BuildAsync(request);
        if (!buildResult.IsSuccess)
        {
            Console.WriteLine($"Erro ao construir XML: [{buildResult.ErrorCode}] {buildResult.ErrorMessage}");
            return 1;
        }

        var buildVal = buildResult.Value!;
        Console.WriteLine($"-> XML gerado com sucesso. Chave de Acesso: {buildVal.ChaveAcesso}");

        Result<AssinaturaResult> assResult;
        
        // Verifica se é um certificado no formato PEM (extensão .pem)
        if (certPath.EndsWith(".pem", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(keyPath))
        {
            Console.WriteLine("2. Assinando XML digitalmente usando arquivos PEM (Certificado + Chave Privada)...");
            assResult = assinador.AssinarComPem(buildVal.XmlNfe, certPath, keyPath);
        }
        else
        {
            Console.WriteLine("2. Assinando XML digitalmente usando certificado PFX/A1...");
            var certBytes = await File.ReadAllBytesAsync(certPath);
            var certBase64 = Convert.ToBase64String(certBytes);
            assResult = assinador.Assinar(buildVal.XmlNfe, certBase64, senha);
        }

        if (!assResult.IsSuccess)
        {
            Console.WriteLine($"Erro ao assinar: [{assResult.ErrorCode}] {assResult.ErrorMessage}");
            return 1;
        }

        var assVal = assResult.Value!;
        Console.WriteLine("-> XML assinado digitalmente com sucesso.");

        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        var outputPath = Path.Combine(outputDir, $"{assVal.ChaveAcesso}-nfe.xml");
        await File.WriteAllTextAsync(outputPath, assVal.XmlAssinado);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Sucesso! XML assinado e salvo em: {outputPath}");
        Console.ResetColor();
        return 0;
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Erro de execução: {ex.Message}");
        Console.ResetColor();
        return 1;
    }
}

if (args[0] is "danfe" or "--danfe")
{
    var xmlPath = ObterArgumento(args, "--xml");
    var outputPath = ObterArgumento(args, "--output");

    if (string.IsNullOrEmpty(xmlPath))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Erro: Parâmetro --xml é obrigatório.");
        Console.ResetColor();
        return 1;
    }

    if (!File.Exists(xmlPath))
    {
        Console.WriteLine($"Erro: XML não encontrado em: {xmlPath}");
        return 1;
    }

    outputPath ??= Path.ChangeExtension(xmlPath, ".pdf");

    try
    {
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        await using var pdfStream = File.Create(outputPath);
        DanfeGenerator.GenerateFromXml(xmlPath, pdfStream);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Sucesso! DANFE gerado em: {outputPath}");
        Console.ResetColor();
        return 0;
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Erro ao gerar DANFE: {ex.Message}");
        Console.ResetColor();
        return 1;
    }
}

Console.WriteLine($"Comando desconhecido: '{args[0]}'. Use --help para ver os comandos disponíveis.");
return 1;

static string? ObterArgumento(string[] args, string flag)
{
    var idx = Array.IndexOf(args, flag);
    if (idx != -1 && idx + 1 < args.Length)
    {
        return args[idx + 1];
    }
    return null;
}

static void MostrarAjuda()
{
    Console.WriteLine("=================================================");
    Console.WriteLine("  nfe-emissor — Utilitário de Emissão de NF-e (.NET 10)");
    Console.WriteLine("=================================================");
    Console.WriteLine();
    Console.WriteLine("Uso:");
    Console.WriteLine("  nfe-emissor emitir --json <caminho> --cert <caminho> [--key <caminho>] [--senha <senha>] [--output-dir <caminho>]");
    Console.WriteLine("  nfe-emissor danfe --xml <procNFe.xml> [--output <danfe.pdf>]");
    Console.WriteLine("  nfe-emissor --danfe --xml <procNFe.xml> [--output <danfe.pdf>]");
    Console.WriteLine();
    Console.WriteLine("Opções:");
    Console.WriteLine("  --json       Caminho para o JSON da nota fiscal contendo os dados fiscais.");
    Console.WriteLine("  --cert       Caminho para o certificado digital A1 (.pfx, .p12 ou .pem).");
    Console.WriteLine("  --key        Caminho para a chave privada (.key ou .pem) caso use certificado PEM.");
    Console.WriteLine("  --senha      Senha do certificado digital (opcional).");
    Console.WriteLine("  --output-dir Diretório onde o XML assinado será salvo (Padrão: 'out').");
    Console.WriteLine("  --xml        Caminho do XML processado/autorizado (procNFe.xml) para gerar DANFE.");
    Console.WriteLine("  --output     Caminho do PDF de saída.");
    Console.WriteLine();
}

static void ConfigurarLicencaQuestPdf()
{
    QuestPDF.Settings.License = LicenseType.Community;
}
