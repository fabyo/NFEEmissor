using Nfe.Shared;

namespace Nfe.Core;

public static class NfeRequestRules
{
    public const string HomologacaoDestinatarioNome = "NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL";

    public static Result<EmitirNfeRequest> NormalizeAndValidate(EmitirNfeRequest request)
    {
        var normalized = Normalize(request);
        var errors = new List<string>();

        if (normalized.AmbienteEmissao is not ("1" or "2"))
            errors.Add("AmbienteEmissao deve ser 1 (producao) ou 2 (homologacao).");

        if (!long.TryParse(normalized.Serie, out var serie) || serie < 0 || serie > 889)
            errors.Add("Serie deve estar entre 0 e 889.");

        if (normalized.NumeroNfe is < 1 or > 999999999)
            errors.Add("NumeroNfe deve estar entre 1 e 999999999.");

        if (!CnpjValido(normalized.Emitente.Cnpj))
            errors.Add("CNPJ do emitente invalido.");

        var ufEmitente = normalized.Emitente.Endereco.Uf.ToUpperInvariant();
        if (ufEmitente == "SP" && !InscricaoEstadualSpValida(normalized.Emitente.InscricaoEstadual))
            errors.Add("Inscricao Estadual do emitente invalida para SP.");

        if (!string.IsNullOrWhiteSpace(normalized.Destinatario.Cnpj) && !CnpjValido(normalized.Destinatario.Cnpj))
            errors.Add("CNPJ do destinatario invalido.");

        if (!string.IsNullOrWhiteSpace(normalized.Destinatario.Cpf) && !DocumentoValido(normalized.Destinatario.Cpf, 11))
            errors.Add("CPF do destinatario invalido.");

        if (normalized.Destinatario.Cnpj is not null && normalized.Destinatario.Cpf is not null)
            errors.Add("Informe CNPJ ou CPF do destinatario, nao ambos.");

        if (normalized.AmbienteEmissao == "2" &&
            normalized.Destinatario.NomeRazaoSocial != HomologacaoDestinatarioNome)
            errors.Add($"Em homologacao, a razao social do destinatario deve ser '{HomologacaoDestinatarioNome}'.");

        if (normalized.Produtos.Count == 0)
            errors.Add("A NF-e deve conter ao menos um produto.");

        if (normalized.Produtos.Any(p => p.IndicadorComposicaoTotal is not ("0" or "1")))
            errors.Add("IndicadorComposicaoTotal deve ser 0 (nao compoe total) ou 1 (compoe total).");

        foreach (var produto in normalized.Produtos)
        {
            if (!GtinValidator.IsValid(produto.CodigoEan))
                errors.Add($"Produto {produto.CodigoProduto}: CodigoEan deve ser GTIN-8, GTIN-12, GTIN-13, GTIN-14 valido ou SEM GTIN.");

            if (!GtinValidator.IsValid(produto.CodigoEanTributavel))
                errors.Add($"Produto {produto.CodigoProduto}: CodigoEanTributavel deve ser GTIN-8, GTIN-12, GTIN-13, GTIN-14 valido ou SEM GTIN.");

            var ibsCbs = produto.Impostos.IbsCbs;
            if (ibsCbs == null) continue;

            if (ibsCbs.Cst.OnlyDigits().Length != 3)
                errors.Add("IBSCBS.Cst deve conter 3 digitos.");

            if (ibsCbs.CodigoClassificacaoTributaria.OnlyDigits().Length != 6)
                errors.Add("IBSCBS.CodigoClassificacaoTributaria deve conter 6 digitos.");

            if (ibsCbs.BaseCalculo < 0)
                errors.Add("IBSCBS.BaseCalculo nao pode ser negativo.");

            ValidarTributoReforma(ibsCbs.IbsUf, "IBSCBS.IbsUf", errors);
            ValidarTributoReforma(ibsCbs.IbsMunicipio, "IBSCBS.IbsMunicipio", errors);
            ValidarTributoReforma(ibsCbs.Cbs, "IBSCBS.Cbs", errors);
        }

        if (errors.Count > 0)
            return Result<EmitirNfeRequest>.Failure("NFE_VALIDACAO_FALHOU", string.Join(" ", errors));

        return Result<EmitirNfeRequest>.Success(normalized);
    }

    public static EmitirNfeRequest Normalize(EmitirNfeRequest request)
    {
        if (request.AmbienteEmissao != "2" ||
            request.Destinatario.NomeRazaoSocial == HomologacaoDestinatarioNome)
        {
            return request;
        }

        return request with
        {
            Destinatario = request.Destinatario with
            {
                NomeRazaoSocial = HomologacaoDestinatarioNome
            }
        };
    }

    private static bool DocumentoValido(string value, int length)
    {
        var digits = value.OnlyDigits();
        return digits.Length == length && digits.Distinct().Count() > 1;
    }

    private static bool CnpjValido(string value)
    {
        var cnpj = value.OnlyAlphaNumericUpper();
        if (cnpj.Length != 14 || cnpj.Distinct().Count() == 1)
            return false;

        if (!char.IsDigit(cnpj[12]) || !char.IsDigit(cnpj[13]))
            return false;

        var primeiro = CalcularDigitoCnpj(cnpj[..12], new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 });
        var segundo = CalcularDigitoCnpj(cnpj[..12] + primeiro, new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 });

        return cnpj[12] == (char)('0' + primeiro) &&
               cnpj[13] == (char)('0' + segundo);
    }

    private static int CalcularDigitoCnpj(string baseCnpj, int[] pesos)
    {
        var soma = 0;
        for (var i = 0; i < pesos.Length; i++)
            soma += ValorAlfanumericoCnpj(baseCnpj[i]) * pesos[i];

        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }

    private static int ValorAlfanumericoCnpj(char c)
    {
        if (c is >= '0' and <= '9') return c - '0';
        if (c is >= 'A' and <= 'Z') return c - 48;
        throw new ArgumentException($"Caractere inválido no CNPJ: {c}");
    }

    private static bool InscricaoEstadualSpValida(string value)
    {
        var digits = value.OnlyDigits();
        if (digits.Length != 12 || digits.Distinct().Count() == 1)
            return false;

        var primeiroDigito = CalcularDigitoSp(digits, new[] { 1, 3, 4, 5, 6, 7, 8, 10 }, 8);
        var segundoDigito = CalcularDigitoSp(digits, new[] { 3, 2, 10, 9, 8, 7, 6, 5, 4, 3, 2 }, 11);

        return digits[8] == (char)('0' + primeiroDigito) &&
               digits[11] == (char)('0' + segundoDigito);
    }

    private static int CalcularDigitoSp(string digits, int[] pesos, int quantidade)
    {
        var soma = 0;
        for (var i = 0; i < quantidade; i++)
            soma += (digits[i] - '0') * pesos[i];

        return soma % 11 % 10;
    }

    private static void ValidarTributoReforma(TributoReformaRequest? tributo, string prefixo, List<string> errors)
    {
        if (tributo == null) return;

        if (tributo.Aliquota < 0) errors.Add($"{prefixo}.Aliquota nao pode ser negativa.");
        if (tributo.Valor < 0) errors.Add($"{prefixo}.Valor nao pode ser negativo.");
        if (tributo.PercentualDiferimento < 0) errors.Add($"{prefixo}.PercentualDiferimento nao pode ser negativo.");
        if (tributo.ValorDiferimento < 0) errors.Add($"{prefixo}.ValorDiferimento nao pode ser negativo.");
        if (tributo.ValorDevolucaoTributo < 0) errors.Add($"{prefixo}.ValorDevolucaoTributo nao pode ser negativo.");
        if (tributo.PercentualReducaoAliquota < 0) errors.Add($"{prefixo}.PercentualReducaoAliquota nao pode ser negativo.");
        if (tributo.AliquotaEfetiva < 0) errors.Add($"{prefixo}.AliquotaEfetiva nao pode ser negativa.");

        if (tributo.PercentualDiferimento > 100) errors.Add($"{prefixo}.PercentualDiferimento nao pode ser maior que 100.");
        if (tributo.PercentualReducaoAliquota > 100) errors.Add($"{prefixo}.PercentualReducaoAliquota nao pode ser maior que 100.");
    }
}
