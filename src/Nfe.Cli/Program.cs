using System.Text.Json;
using Nfe.Core;
using Nfe.Shared;

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
    var validarXsd = TemFlag(args, "--validar-xsd");
    var schemasPath = ObterArgumento(args, "--schemas-path") ?? "schemas/v4";
    var tiposBasicosSchema = ObterArgumento(args, "--tipos-basicos-schema");
    var nfeSchema = ObterArgumento(args, "--nfe-schema");

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

        if (validarXsd)
        {
            Console.WriteLine("3. Validando XML assinado contra schemas XSD...");
            var validator = new NfeXsdValidator(schemasPath, tiposBasicosSchema, nfeSchema);
            var xsdResult = assVal.XmlAssinado.ValidarXsd(validator);
            if (!xsdResult.IsSuccess)
            {
                Console.WriteLine($"Erro de validação XSD: [{xsdResult.ErrorCode}] {xsdResult.ErrorMessage}");
                return 1;
            }

            Console.WriteLine("-> XML assinado validado contra XSD com sucesso.");
        }

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

if (args[0] is "cancelar" or "cce" or "inutilizar")
{
    var jsonPath = ObterArgumento(args, "--json");
    var certPath = ObterArgumento(args, "--cert");
    var keyPath = ObterArgumento(args, "--key");
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

        var eventoService = new NfeEventoService(new NfeAssinadorService());
        var jsonContent = await File.ReadAllTextAsync(jsonPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        Result<string> xmlResult;
        string elementoAssinavel;
        string nomeArquivo;

        if (args[0] == "cancelar")
        {
            var request = JsonSerializer.Deserialize<CancelarNfeRequest>(jsonContent, options);
            if (request == null)
            {
                Console.WriteLine("Erro: Não foi possível desserializar o JSON de cancelamento.");
                return 1;
            }

            xmlResult = eventoService.MontarXmlCancelamento(request);
            elementoAssinavel = "infEvento";
            nomeArquivo = $"{request.ChaveAcesso.OnlyAlphaNumericUpper()}-cancelamento.xml";
        }
        else if (args[0] == "cce")
        {
            var request = JsonSerializer.Deserialize<CartaCorrecaoRequest>(jsonContent, options);
            if (request == null)
            {
                Console.WriteLine("Erro: Não foi possível desserializar o JSON de CC-e.");
                return 1;
            }

            xmlResult = eventoService.MontarXmlCartaCorrecao(request);
            elementoAssinavel = "infEvento";
            nomeArquivo = $"{request.ChaveAcesso.OnlyAlphaNumericUpper()}-cce-{request.SequenciaEvento:D2}.xml";
        }
        else
        {
            var request = JsonSerializer.Deserialize<InutilizarNumeracaoRequest>(jsonContent, options);
            if (request == null)
            {
                Console.WriteLine("Erro: Não foi possível desserializar o JSON de inutilização.");
                return 1;
            }

            xmlResult = eventoService.MontarXmlInutilizacao(request);
            elementoAssinavel = "infInut";
            nomeArquivo = $"inutilizacao-{request.Serie}-{request.NumeroInicial}-{request.NumeroFinal}.xml";
        }

        if (!xmlResult.IsSuccess)
        {
            Console.WriteLine($"Erro ao montar XML: [{xmlResult.ErrorCode}] {xmlResult.ErrorMessage}");
            return 1;
        }

        var assinador = new NfeAssinadorService();
        Result<AssinaturaResult> assResult;
        if (certPath.EndsWith(".pem", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(keyPath))
        {
            assResult = assinador.AssinarElementoComPemConteudo(
                xmlResult.Value!,
                elementoAssinavel,
                "ID",
                await File.ReadAllTextAsync(certPath),
                await File.ReadAllTextAsync(keyPath));
        }
        else
        {
            var certBytes = await File.ReadAllBytesAsync(certPath);
            assResult = assinador.AssinarElemento(xmlResult.Value!, elementoAssinavel, "ID", Convert.ToBase64String(certBytes), senha);
        }

        if (!assResult.IsSuccess)
        {
            Console.WriteLine($"Erro ao assinar: [{assResult.ErrorCode}] {assResult.ErrorMessage}");
            return 1;
        }

        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        var outputPath = Path.Combine(outputDir, nomeArquivo);
        await File.WriteAllTextAsync(outputPath, assResult.Value!.XmlAssinado);

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

static bool TemFlag(string[] args, string flag)
    => args.Any(arg => string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase));

static void MostrarAjuda()
{
    Console.WriteLine("=================================================");
    Console.WriteLine("  nfe-emissor — Utilitário de Emissão de NF-e (.NET 10)");
    Console.WriteLine("=================================================");
    Console.WriteLine();
    Console.WriteLine("Uso:");
    Console.WriteLine("  nfe-emissor emitir --json <caminho> --cert <caminho> [--key <caminho>] [--senha <senha>] [--output-dir <caminho>]");
    Console.WriteLine("  nfe-emissor cancelar --json <caminho> --cert <caminho> [--key <caminho>] [--senha <senha>] [--output-dir <caminho>]");
    Console.WriteLine("  nfe-emissor cce --json <caminho> --cert <caminho> [--key <caminho>] [--senha <senha>] [--output-dir <caminho>]");
    Console.WriteLine("  nfe-emissor inutilizar --json <caminho> --cert <caminho> [--key <caminho>] [--senha <senha>] [--output-dir <caminho>]");
    Console.WriteLine();
    Console.WriteLine("Opções:");
    Console.WriteLine("  --json       Caminho para o JSON da NF-e, evento ou inutilização.");
    Console.WriteLine("  --cert       Caminho para o certificado digital A1 (.pfx, .p12 ou .pem).");
    Console.WriteLine("  --key        Caminho para a chave privada (.key ou .pem) caso use certificado PEM.");
    Console.WriteLine("  --senha      Senha do certificado digital (opcional).");
    Console.WriteLine("  --output-dir Diretório onde o XML assinado será salvo (Padrão: 'out').");
    Console.WriteLine("  --validar-xsd Valida o XML contra XSD antes de assinar (comando emitir).");
    Console.WriteLine("  --schemas-path Diretório dos schemas XSD (Padrão: 'schemas/v4').");
    Console.WriteLine("  --tipos-basicos-schema Nome do XSD tiposBasico a usar.");
    Console.WriteLine("  --nfe-schema Nome do XSD principal da NF-e a usar.");
    Console.WriteLine();
}
