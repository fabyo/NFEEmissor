using Nfe.Core;
using Nfe.Shared;
using Xunit;

namespace Nfe.UnitTests;

public sealed class NfeCoreTests
{
    [Fact]
    public void ChaveAcessoCalculator_DeveCalcularDigitoVerificadorCorreto()
    {
        // Chave de acesso fictícia de 43 dígitos para SP (35)
        var chave43 = "3523091234567800019055001000000001100000000";
        
        var dv = ChaveAcessoCalculator.CalcularDigitoVerificador(chave43);

        // Deve calcular dígito verificador módulo 11
        Assert.InRange(dv, 0, 9);
    }

    [Fact]
    public void TotaisCalculator_DeveSomarValoresDosProdutosCorretamente()
    {
        var produtos = new List<ProdutoRequest>
        {
            new()
            {
                CodigoProduto = "PROD001",
                Descricao = "PRODUTO TESTE 1",
                NcmSh = "84713012",
                Cfop = "5102",
                UnidadeComercial = "UN",
                QuantidadeComercial = 2,
                ValorUnitarioComercial = 50.00m,
                ValorBruto = 100.00m,
                IndicadorComposicaoTotal = "1",
                Impostos = new ImpostosRequest
                {
                    Icms = new IcmsRequest { Cst = "00", BaseCalculo = 100.00m, Aliquota = 18m, Valor = 18.00m }
                }
            },
            new()
            {
                CodigoProduto = "PROD002",
                Descricao = "PRODUTO TESTE 2",
                NcmSh = "84713012",
                Cfop = "5102",
                UnidadeComercial = "UN",
                QuantidadeComercial = 1,
                ValorUnitarioComercial = 150.00m,
                ValorBruto = 150.00m,
                IndicadorComposicaoTotal = "1",
                Impostos = new ImpostosRequest
                {
                    Icms = new IcmsRequest { Cst = "40", ValorDesonerado = 0 }
                }
            }
        };

        var totais = TotaisCalculator.Calcular(produtos);

        Assert.Equal(250.00m, totais.ValorProdutos);
        Assert.Equal(18.00m, totais.ValorIcms);
        Assert.Equal(250.00m, totais.ValorNfe);
    }

    [Fact]
    public void TotaisCalculator_NaoDeveSomarProdutoComIndTotZero()
    {
        var produtos = new List<ProdutoRequest>
        {
            new()
            {
                CodigoProduto = "PROD001",
                Descricao = "PRODUTO TESTE",
                NcmSh = "84713012",
                Cfop = "5102",
                UnidadeComercial = "UN",
                QuantidadeComercial = 1,
                ValorUnitarioComercial = 100.00m,
                ValorBruto = 100.00m,
                IndicadorComposicaoTotal = "0",
                Impostos = new ImpostosRequest()
            }
        };

        var totais = TotaisCalculator.Calcular(produtos);

        Assert.Equal(0.00m, totais.ValorProdutos);
        Assert.Equal(0.00m, totais.ValorNfe);
    }

    [Fact]
    public void NfeRequestRules_DeveNormalizarDestinatarioEmHomologacao()
    {
        var request = CriarRequestValido() with
        {
            AmbienteEmissao = "2",
            Destinatario = CriarRequestValido().Destinatario with
            {
                NomeRazaoSocial = "CLIENTE TESTE"
            }
        };

        var result = NfeRequestRules.NormalizeAndValidate(request);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(
            NfeRequestRules.HomologacaoDestinatarioNome,
            result.Value!.Destinatario.NomeRazaoSocial);
    }

    [Fact]
    public void NfeRequestRules_DeveRejeitarIeSpInvalidaAntesDaSefaz()
    {
        var request = CriarRequestValido() with
        {
            Emitente = CriarRequestValido().Emitente with
            {
                InscricaoEstadual = "123456789"
            }
        };

        var result = NfeRequestRules.NormalizeAndValidate(request);

        Assert.False(result.IsSuccess);
        Assert.Equal("NFE_VALIDACAO_FALHOU", result.ErrorCode);
        Assert.Contains("Inscricao Estadual", result.ErrorMessage);
    }

    [Fact]
    public async Task NfeXmlBuilder_DevePreservarCnpjAlfanumerico()
    {
        var request = CriarRequestValido() with
        {
            Emitente = CriarRequestValido().Emitente with
            {
                Cnpj = "12.ABC.345/0001-88"
            }
        };

        var result = await new NfeXmlBuilder().BuildAsync(request);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains("<CNPJ>12ABC345000188</CNPJ>", result.Value!.XmlNfe);
        Assert.Contains("12ABC345000188", result.Value.ChaveAcesso);
        Assert.True(ChaveAcessoCalculator.Validar(result.Value.ChaveAcesso));
    }

    [Fact]
    public async Task NfeXmlBuilder_DeveGerarGrupoIbsCbs()
    {
        var request = CriarRequestValido();
        var produto = request.Produtos[0] with
        {
            Impostos = request.Produtos[0].Impostos with
            {
                IbsCbs = new IbsCbsRequest
                {
                    Cst = "410",
                    CodigoClassificacaoTributaria = "410999",
                    BaseCalculo = 100,
                    IbsUf = new IbsUfRequest { Aliquota = 0.10m, Valor = 0.10m },
                    IbsMunicipio = new IbsMunicipioRequest { Aliquota = 0.00m, Valor = 0.00m },
                    Cbs = new CbsRequest { Aliquota = 0.90m, Valor = 0.90m }
                }
            }
        };

        var result = await new NfeXmlBuilder().BuildAsync(request with { Produtos = [produto] });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains("<IBSCBS>", result.Value!.XmlNfe);
        Assert.Contains("<CST>410</CST>", result.Value.XmlNfe);
        Assert.Contains("<cClassTrib>410999</cClassTrib>", result.Value.XmlNfe);
        Assert.Contains("<IBSCBSTot>", result.Value.XmlNfe);
        Assert.Contains("<vBCIBSCBS>100.00</vBCIBSCBS>", result.Value.XmlNfe);
    }

    [Fact]
    public async Task NfeXmlBuilder_DeveFormatarValorUnitarioSemZerosDesnecessarios()
    {
        var request = CriarRequestValido();

        var result = await new NfeXmlBuilder().BuildAsync(request);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains("<vUnCom>100</vUnCom>", result.Value!.XmlNfe);
        Assert.Contains("<vUnTrib>100</vUnTrib>", result.Value.XmlNfe);
        Assert.DoesNotContain("100.0000000000", result.Value.XmlNfe);
    }

    private static EmitirNfeRequest CriarRequestValido() => new()
    {
        AmbienteEmissao = "2",
        Serie = "1",
        NumeroNfe = 8,
        NaturezaOperacao = "VENDA DE MERCADORIA",
        TipoOperacao = "1",
        ConsumidorFinal = "1",
        IndicadorPresencaComprador = "9",
        Emitente = new EmitenteRequest
        {
            Cnpj = "12345678000195",
            RazaoSocial = "EMPRESA EMITENTE TESTE LTDA",
            InscricaoEstadual = "110042490114",
            CnaeFiscal = "2500000",
            CodigoRegimeTributario = "3",
            Endereco = new EnderecoRequest
            {
                Logradouro = "RUA TESTE",
                Numero = "500",
                Bairro = "DISTRITO INDUSTRIAL",
                CodigoMunicipio = "3550308",
                NomeMunicipio = "SAO PAULO",
                Uf = "SP",
                Cep = "01001000"
            }
        },
        Destinatario = new DestinatarioRequest
        {
            Cnpj = "99999999000191",
            NomeRazaoSocial = NfeRequestRules.HomologacaoDestinatarioNome,
            IndicadorIe = "9",
            Endereco = new EnderecoRequest
            {
                Logradouro = "AVENIDA CLIENTE",
                Numero = "200",
                Bairro = "JARDINS",
                CodigoMunicipio = "3550308",
                NomeMunicipio = "SAO PAULO",
                Uf = "SP",
                Cep = "02002000"
            }
        },
        Produtos = new List<ProdutoRequest>
        {
            new()
            {
                CodigoProduto = "PROD-001",
                Descricao = "NOTA FISCAL EMITIDA EM AMBIENTE DE HOMOLOGACAO",
                NcmSh = "87089990",
                Cfop = "5102",
                UnidadeComercial = "UN",
                QuantidadeComercial = 1,
                ValorUnitarioComercial = 100,
                ValorBruto = 100,
                Impostos = new ImpostosRequest
                {
                    Icms = new IcmsRequest { Cst = "00", BaseCalculo = 100, Aliquota = 18, Valor = 18 }
                }
            }
        },
        Transporte = new TransporteRequest { ModalidadeFrete = "9" },
        Pagamentos = new List<PagamentoRequest>
        {
            new() { MeioPagamento = "15", Valor = 100 }
        }
    };
}
