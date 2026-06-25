using Nfe.Shared;

namespace Nfe.Core;

/// <summary>
/// Agrega os totais dos itens para preencher o grupo ICMSTot da NF-e.
/// Todos os valores são arredondados em 2 casas conforme regra SEFAZ.
/// </summary>
public static class TotaisCalculator
{
    public static NfeTotais Calcular(IEnumerable<ProdutoRequest> produtos)
    {
        var t = new NfeTotais();

        foreach (var p in produtos)
        {
            var icms = p.Impostos.Icms;
            var pis = p.Impostos.Pis;
            var cofins = p.Impostos.Cofins;
            var ipi = p.Impostos.Ipi;
            var ibsCbs = p.Impostos.IbsCbs;

            t.BaseCalculoIcms += icms?.BaseCalculo ?? 0m;
            t.ValorIcms += icms?.Valor ?? 0m;
            t.ValorIcmsDesonerado += icms?.ValorDesonerado ?? 0m;
            t.BaseCalculoIcmsSt += icms?.BaseCalculoSt ?? 0m;
            t.ValorIcmsSt += icms?.ValorSt ?? 0m;
            t.ValorFcp += icms?.ValorFcp ?? 0m;
            t.ValorIpi += ipi?.Valor ?? 0m;
            t.ValorPis += pis?.Valor ?? 0m;
            t.ValorCofins += cofins?.Valor ?? 0m;
            t.ValorFrete += p.ValorFrete;
            t.ValorSeguro += p.ValorSeguro;
            t.ValorDesconto += p.ValorDesconto;
            t.ValorOutrasDespesas += p.ValorOutrasDespesas;
            if (ibsCbs != null)
            {
                t.TemIbsCbs = true;
                t.BaseCalculoIbsCbs += ibsCbs.BaseCalculo;
                t.ValorIbsUf += ibsCbs.IbsUf?.Valor ?? 0m;
                t.ValorDiferimentoIbsUf += ibsCbs.IbsUf?.ValorDiferimento ?? 0m;
                t.ValorDevolucaoTributoIbsUf += ibsCbs.IbsUf?.ValorDevolucaoTributo ?? 0m;
                t.ValorIbsMunicipio += ibsCbs.IbsMunicipio?.Valor ?? 0m;
                t.ValorDiferimentoIbsMunicipio += ibsCbs.IbsMunicipio?.ValorDiferimento ?? 0m;
                t.ValorDevolucaoTributoIbsMunicipio += ibsCbs.IbsMunicipio?.ValorDevolucaoTributo ?? 0m;
                t.ValorCbsReforma += ibsCbs.Cbs?.Valor ?? 0m;
                t.ValorDiferimentoCbs += ibsCbs.Cbs?.ValorDiferimento ?? 0m;
                t.ValorDevolucaoTributoCbs += ibsCbs.Cbs?.ValorDevolucaoTributo ?? 0m;
            }

            // Produtos que compõem o valor total da NF-e (indTot = 1)
            if (p.IndicadorComposicaoTotal == "1")
                t.ValorProdutos += p.ValorBruto;
        }

        // vNF = vProd + vFrete + vSeg + vOutro + vIPI + vIPIDevol - vDesc - vDesICMS
        // (sem IPI nas notas de serviço / ISSQN)
        t.ValorNfe = t.ValorProdutos
                   + t.ValorFrete
                   + t.ValorSeguro
                   + t.ValorOutrasDespesas
                   + t.ValorIpi
                   - t.ValorDesconto
                   - t.ValorIcmsDesonerado;

        // Arredonda tudo em 2 casas
        return t.Arredondar();
    }
}

public sealed class NfeTotais
{
    public decimal BaseCalculoIcms { get; set; }
    public decimal ValorIcms { get; set; }
    public decimal ValorIcmsDesonerado { get; set; }
    public decimal BaseCalculoIcmsSt { get; set; }
    public decimal ValorIcmsSt { get; set; }
    public decimal ValorFcp { get; set; }
    public decimal ValorFcpSt { get; set; }
    public decimal ValorFcpStRetido { get; set; }
    public decimal ValorIpi { get; set; }
    public decimal ValorIpiDevolvido { get; set; }
    public decimal ValorPis { get; set; }
    public decimal ValorCofins { get; set; }
    public decimal ValorFrete { get; set; }
    public decimal ValorSeguro { get; set; }
    public decimal ValorDesconto { get; set; }
    public decimal ValorOutrasDespesas { get; set; }
    public decimal ValorProdutos { get; set; }
    public decimal ValorNfe { get; set; }
    public bool TemIbsCbs { get; set; }
    public decimal BaseCalculoIbsCbs { get; set; }
    public decimal ValorIbsUf { get; set; }
    public decimal ValorDiferimentoIbsUf { get; set; }
    public decimal ValorDevolucaoTributoIbsUf { get; set; }
    public decimal ValorIbsMunicipio { get; set; }
    public decimal ValorDiferimentoIbsMunicipio { get; set; }
    public decimal ValorDevolucaoTributoIbsMunicipio { get; set; }
    public decimal ValorCbsReforma { get; set; }
    public decimal ValorDiferimentoCbs { get; set; }
    public decimal ValorDevolucaoTributoCbs { get; set; }

    public NfeTotais Arredondar()
    {
        BaseCalculoIcms = Decimal.Round(BaseCalculoIcms, 2);
        ValorIcms = Decimal.Round(ValorIcms, 2);
        ValorIcmsDesonerado = Decimal.Round(ValorIcmsDesonerado, 2);
        BaseCalculoIcmsSt = Decimal.Round(BaseCalculoIcmsSt, 2);
        ValorIcmsSt = Decimal.Round(ValorIcmsSt, 2);
        ValorFcp = Decimal.Round(ValorFcp, 2);
        ValorFcpSt = Decimal.Round(ValorFcpSt, 2);
        ValorFcpStRetido = Decimal.Round(ValorFcpStRetido, 2);
        ValorIpi = Decimal.Round(ValorIpi, 2);
        ValorIpiDevolvido = Decimal.Round(ValorIpiDevolvido, 2);
        ValorPis = Decimal.Round(ValorPis, 2);
        ValorCofins = Decimal.Round(ValorCofins, 2);
        ValorFrete = Decimal.Round(ValorFrete, 2);
        ValorSeguro = Decimal.Round(ValorSeguro, 2);
        ValorDesconto = Decimal.Round(ValorDesconto, 2);
        ValorOutrasDespesas = Decimal.Round(ValorOutrasDespesas, 2);
        ValorProdutos = Decimal.Round(ValorProdutos, 2);
        ValorNfe = Decimal.Round(ValorNfe, 2);
        BaseCalculoIbsCbs = Decimal.Round(BaseCalculoIbsCbs, 2);
        ValorIbsUf = Decimal.Round(ValorIbsUf, 2);
        ValorDiferimentoIbsUf = Decimal.Round(ValorDiferimentoIbsUf, 2);
        ValorDevolucaoTributoIbsUf = Decimal.Round(ValorDevolucaoTributoIbsUf, 2);
        ValorIbsMunicipio = Decimal.Round(ValorIbsMunicipio, 2);
        ValorDiferimentoIbsMunicipio = Decimal.Round(ValorDiferimentoIbsMunicipio, 2);
        ValorDevolucaoTributoIbsMunicipio = Decimal.Round(ValorDevolucaoTributoIbsMunicipio, 2);
        ValorCbsReforma = Decimal.Round(ValorCbsReforma, 2);
        ValorDiferimentoCbs = Decimal.Round(ValorDiferimentoCbs, 2);
        ValorDevolucaoTributoCbs = Decimal.Round(ValorDevolucaoTributoCbs, 2);
        return this;
    }
}
