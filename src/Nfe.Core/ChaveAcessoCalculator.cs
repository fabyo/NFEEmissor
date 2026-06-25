namespace Nfe.Core;

/// <summary>
/// Calcula a chave de acesso de 44 posições da NF-e conforme Manual SEFAZ.
/// Estrutura: cUF(2) + AAMM(4) + CNPJ(14) + mod(2) + serie(3) + nNF(9) + tpEmis(1) + cNF(8) + cDV(1)
/// </summary>
public static class ChaveAcessoCalculator
{
    /// <param name="cUf">Código IBGE da UF emitente (ex: 35 = SP)</param>
    /// <param name="dataEmissao">Data de emissão</param>
    /// <param name="cnpjEmitente">CNPJ sem pontuação (14 caracteres alfanuméricos)</param>
    /// <param name="modelo">55 = NF-e | 65 = NFC-e</param>
    /// <param name="serie">Série da nota (ex: "001")</param>
    /// <param name="numeroNfe">Número sequencial da NF-e</param>
    /// <param name="tipoEmissao">1 = Normal | 2 = Conting. FS-IA | ...</param>
    /// <param name="codigoNumerico">Código numérico aleatório de 8 dígitos (cNF). Se null, gerado automaticamente.</param>
    public static ChaveAcessoResult Calcular(
        int cUf,
        DateTime dataEmissao,
        string cnpjEmitente,
        int modelo,
        string serie,
        long numeroNfe,
        int tipoEmissao = 1,
        string? codigoNumerico = null)
    {
        cnpjEmitente = cnpjEmitente.OnlyAlphaNumericUpper();

        if (cnpjEmitente.Length != 14)
            throw new ArgumentException("CNPJ deve ter 14 caracteres alfanuméricos.", nameof(cnpjEmitente));

        // cNF — 8 dígitos aleatórios (se não informado, gerar)
        var cNf = codigoNumerico?.OnlyDigits().PadLeft(8, '0')
                  ?? Random.Shared.Next(10_000_000, 99_999_999).ToString();

        var aamm = dataEmissao.ToString("yyMM");

        // Monta as primeiras 43 posições (sem o dígito verificador)
        var semDv = string.Concat(
            cUf.ToString().PadLeft(2, '0'),
            aamm,
            cnpjEmitente,
            modelo.ToString().PadLeft(2, '0'),
            serie.OnlyDigits().PadLeft(3, '0'),
            numeroNfe.ToString().PadLeft(9, '0'),
            tipoEmissao.ToString(),
            cNf
        );

        if (semDv.Length != 43)
            throw new InvalidOperationException(
                $"Chave base deve ter 43 posições. Gerou: {semDv.Length} — '{semDv}'");

        var dv = CalcularDigitoVerificador(semDv);
        var chaveCompleta = semDv + dv;

        return new ChaveAcessoResult
        {
            Chave = chaveCompleta,
            CodigoNumerico = cNf,
            DigitoVerificador = dv
        };
    }

    /// <summary>
    /// Módulo 11 com pesos 2..9 ciclicamente da direita para esquerda.
    /// Resto 0 ou 1 → dígito = 0 (conforme NT 2013.005 SEFAZ).
    /// </summary>
    public static int CalcularDigitoVerificador(string chave43)
    {
        chave43 = chave43.OnlyAlphaNumericUpper();
        if (chave43.Length != 43)
            throw new ArgumentException("Chave deve ter 43 posições.", nameof(chave43));

        var soma = 0;
        var peso = 2;

        for (var i = chave43.Length - 1; i >= 0; i--)
        {
            soma += ValorAlfanumerico(chave43[i]) * peso;
            peso = peso == 9 ? 2 : peso + 1;
        }

        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }

    /// <summary>Valida se uma chave de 44 posições tem dígito verificador correto.</summary>
    public static bool Validar(string chave44)
    {
        chave44 = chave44.OnlyAlphaNumericUpper();
        if (chave44.Length != 44) return false;
        if (!char.IsDigit(chave44[43])) return false;
        var dvCalculado = CalcularDigitoVerificador(chave44[..43]);
        return dvCalculado == int.Parse(chave44[43].ToString());
    }

    private static int ValorAlfanumerico(char c)
    {
        if (c is >= '0' and <= '9') return c - '0';
        if (c is >= 'A' and <= 'Z') return c - 48;
        throw new ArgumentException($"Caractere inválido na chave: {c}");
    }
}

public sealed record ChaveAcessoResult
{
    public required string Chave { get; init; }
    public required string CodigoNumerico { get; init; }
    public required int DigitoVerificador { get; init; }
}
