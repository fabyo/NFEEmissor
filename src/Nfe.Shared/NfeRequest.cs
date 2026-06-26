namespace Nfe.Shared;

// ─────────────────────────────────────────────
// DTO raiz — o que chega via JSON na API
// ─────────────────────────────────────────────
public sealed record EmitirNfeRequest
{
    /// <summary>1 = Produção | 2 = Homologação</summary>
    public required string AmbienteEmissao { get; init; }

    /// <summary>Série da NF-e (000 a 889)</summary>
    public required string Serie { get; init; }

    /// <summary>Número da NF-e (1 a 999999999)</summary>
    public required long NumeroNfe { get; init; }

    /// <summary>Natureza da operação</summary>
    public required string NaturezaOperacao { get; init; }

    /// <summary>1 = Entrada | 2 = Saída</summary>
    public required string TipoOperacao { get; init; }

    /// <summary>0 = Sem geração de DANFE | 1 = DANFE normal | 4 = DANFE NFC-e</summary>
    public string FormatoImpressaoDanfe { get; init; } = "1";

    /// <summary>1 = Normal | 2 = Contingência FS-IA | ... (ver tabela SEFAZ)</summary>
    public string TipoEmissao { get; init; } = "1";

    /// <summary>0 = Não gera Fisco | 1 = Gera Fisco</summary>
    public string FinalidadeEmissao { get; init; } = "1";

    /// <summary>0 = Normal | 1 = Consumidor Final</summary>
    public string ConsumidorFinal { get; init; } = "0";

    /// <summary>0 = Não presencial (Internet) | 1 = Operação presencial ... (ver tabela)</summary>
    public string IndicadorPresencaComprador { get; init; } = "0";

    public required EmitenteRequest Emitente { get; init; }
    public required DestinatarioRequest Destinatario { get; init; }
    public required List<ProdutoRequest> Produtos { get; init; }
    public required TransporteRequest Transporte { get; init; }
    public required List<PagamentoRequest> Pagamentos { get; init; }
    public List<string> NotasReferenciadas { get; init; } = [];
    public string? InformacoesAdicionais { get; init; }
    public string? InformacoesAdicionaisFisco { get; init; }
}

// ─────────────────────────────────────────────
// Emitente
// ─────────────────────────────────────────────
public sealed record EmitenteRequest
{
    public required string Cnpj { get; init; }
    public required string RazaoSocial { get; init; }
    public string? NomeFantasia { get; init; }
    public required string InscricaoEstadual { get; init; }
    public string? InscricaoEstadualSt { get; init; }
    public string? InscricaoMunicipal { get; init; }
    public required string CnaeFiscal { get; init; }

    /// <summary>CRT: 1 = Simples Nacional | 2 = Simples Nacional – Excesso | 3 = Regime Normal</summary>
    public required string CodigoRegimeTributario { get; init; }

    public required EnderecoRequest Endereco { get; init; }
}

// ─────────────────────────────────────────────
// Destinatário
// ─────────────────────────────────────────────
public sealed record DestinatarioRequest
{
    /// <summary>Informar CNPJ ou CPF, não ambos</summary>
    public string? Cnpj { get; init; }
    public string? Cpf { get; init; }
    public string? IdEstrangeiro { get; init; }
    public required string NomeRazaoSocial { get; init; }
    public string? InscricaoEstadual { get; init; }

    /// <summary>
    /// 1 = Contribuinte ICMS | 2 = Contribuinte isento | 9 = Não contribuinte
    /// </summary>
    public required string IndicadorIe { get; init; }

    public string? Email { get; init; }
    public required EnderecoRequest Endereco { get; init; }
}

// ─────────────────────────────────────────────
// Endereço
// ─────────────────────────────────────────────
public sealed record EnderecoRequest
{
    public required string Logradouro { get; init; }
    public required string Numero { get; init; }
    public string? Complemento { get; init; }
    public required string Bairro { get; init; }
    public required string CodigoMunicipio { get; init; }
    public required string NomeMunicipio { get; init; }
    public required string Uf { get; init; }
    public required string Cep { get; init; }

    /// <summary>1058 = Brasil</summary>
    public string CodigoPais { get; init; } = "1058";
    public string NomePais { get; init; } = "Brasil";
    public string? Telefone { get; init; }
}

// ─────────────────────────────────────────────
// Produto / Item da NF-e
// ─────────────────────────────────────────────
public sealed record ProdutoRequest
{
    public required string CodigoProduto { get; init; }
    public string? CodigoEan { get; init; }
    public string? CodigoBarra { get; init; }
    public required string Descricao { get; init; }
    public required string NcmSh { get; init; }
    public string? CodigoBeneficioFiscal { get; init; }
    public string? Cest { get; init; }
    public string? CodigoExTipi { get; init; }

    /// <summary>CFOP — ex: 5102, 6102</summary>
    public required string Cfop { get; init; }

    public required string UnidadeComercial { get; init; }
    public required decimal QuantidadeComercial { get; init; }
    public required decimal ValorUnitarioComercial { get; init; }
    public required decimal ValorBruto { get; init; }

    public string? CodigoEanTributavel { get; init; }
    public string UnidadeTributavel { get; init; } = "";
    public decimal QuantidadeTributavel { get; init; }
    public decimal ValorUnitarioTributavel { get; init; }

    public decimal ValorFrete { get; init; }
    public decimal ValorSeguro { get; init; }
    public decimal ValorDesconto { get; init; }
    public decimal ValorOutrasDespesas { get; init; }

    /// <summary>0 = Não compõe valor total | 1 = Compõe valor total</summary>
    public string IndicadorComposicaoTotal { get; init; } = "1";

    public required ImpostosRequest Impostos { get; init; }
}

// ─────────────────────────────────────────────
// Impostos por item
// ─────────────────────────────────────────────
public sealed record ImpostosRequest
{
    public IcmsRequest? Icms { get; init; }
    public PisRequest? Pis { get; init; }
    public CofinsRequest? Cofins { get; init; }
    public IpiRequest? Ipi { get; init; }
    public IssqnRequest? Issqn { get; init; }
    public IbsCbsRequest? IbsCbs { get; init; }
}

public sealed record IcmsRequest
{
    /// <summary>
    /// CST ICMS — 00,10,20,30,40,41,50,51,60,70,90 (tributado)
    /// CSOSN — 102,103,300,400,500,900 (Simples Nacional)
    /// </summary>
    public required string Cst { get; init; }

    /// <summary>Origem: 0=Nacional | 1=Estrangeira (importação direta) | ...</summary>
    public string Origem { get; init; } = "0";

    public decimal BaseCalculo { get; init; }

    /// <summary>Modalidade BC: 3 = Valor da operação (mais comum)</summary>
    public string ModalidadeBaseCalculo { get; init; } = "3";

    public decimal Aliquota { get; init; }
    public decimal Valor { get; init; }

    // ST
    public decimal BaseCalculoSt { get; init; }
    public string ModalidadeBaseCalculoSt { get; init; } = "4";
    public decimal AliquotaSt { get; init; }
    public decimal ValorSt { get; init; }
    public decimal MvaAjustado { get; init; }

    // Diferimento / redução
    public decimal PercentualReducaoBaseCalculo { get; init; }
    public decimal PercentualDiferimento { get; init; }
    public decimal ValorIcmsDiferido { get; init; }
    public decimal ValorIcmsOperacao { get; init; }

    // ICMS desonerado
    public decimal ValorDesonerado { get; init; }
    public string? MotivoDesoneracaoSt { get; init; }

    // FCP (Fundo de Combate à Pobreza)
    public decimal PercentualFcp { get; init; }
    public decimal ValorFcp { get; init; }
}

public sealed record PisRequest
{
    /// <summary>CST PIS — 01,02,03,04,05,06,07,08,09,49,50,99</summary>
    public required string Cst { get; init; }
    public decimal BaseCalculo { get; init; }
    public decimal Aliquota { get; init; }
    public decimal Valor { get; init; }

    // Para CST 02/05 (por unidade)
    public decimal QuantidadeVendida { get; init; }
    public decimal AliquotaReais { get; init; }
}

public sealed record CofinsRequest
{
    public required string Cst { get; init; }
    public decimal BaseCalculo { get; init; }
    public decimal Aliquota { get; init; }
    public decimal Valor { get; init; }
    public decimal QuantidadeVendida { get; init; }
    public decimal AliquotaReais { get; init; }
}

public sealed record IpiRequest
{
    public required string CodigoEnquadramentoLegal { get; init; }
    public string? Cnpj { get; init; }
    public string? DataSaida { get; init; }

    /// <summary>CST IPI: 00,49,50,99</summary>
    public required string Cst { get; init; }
    public decimal BaseCalculo { get; init; }
    public decimal Aliquota { get; init; }
    public decimal Valor { get; init; }

    // Tributação por unidade
    public decimal QuantidadeTotal { get; init; }
    public decimal ValorPorUnidade { get; init; }
}

public sealed record IssqnRequest
{
    public decimal BaseCalculo { get; init; }
    public decimal Aliquota { get; init; }
    public decimal Valor { get; init; }
    public required string CodigoMunicipioFatoGerador { get; init; }
    public required string ListaServico { get; init; }
    public decimal ValorDeducao { get; init; }
    public decimal ValorOutrasRetencoes { get; init; }
    public decimal ValorDescontoIncondicionado { get; init; }
    public decimal ValorDescontoCondicionado { get; init; }
    public decimal ValorRetencaoIss { get; init; }
    public string? IndicadorExigibilidadeIss { get; init; }
    public string? CodigoServico { get; init; }
    public string? CodigoMunicipioIncidencia { get; init; }
    public string? CodigoPais { get; init; }
    public string? NumeroProcesso { get; init; }
    public string? IndicadorIncentivo { get; init; }
}

public sealed record IbsCbsRequest
{
    /// <summary>CST IBS/CBS — ex: 000, 200, 410.</summary>
    public required string Cst { get; init; }

    /// <summary>cClassTrib — classificação tributária IBS/CBS.</summary>
    public required string CodigoClassificacaoTributaria { get; init; }

    public decimal BaseCalculo { get; init; }
    public IbsUfRequest? IbsUf { get; init; }
    public IbsMunicipioRequest? IbsMunicipio { get; init; }
    public CbsRequest? Cbs { get; init; }
}

public sealed record IbsUfRequest : TributoReformaRequest;
public sealed record IbsMunicipioRequest : TributoReformaRequest;
public sealed record CbsRequest : TributoReformaRequest;

public abstract record TributoReformaRequest
{
    public decimal Aliquota { get; init; }
    public decimal Valor { get; init; }
    public decimal PercentualDiferimento { get; init; }
    public decimal ValorDiferimento { get; init; }
    public decimal ValorDevolucaoTributo { get; init; }
    public decimal PercentualReducaoAliquota { get; init; }
    public decimal AliquotaEfetiva { get; init; }
}

// ─────────────────────────────────────────────
// Transporte
// ─────────────────────────────────────────────
public sealed record TransporteRequest
{
    /// <summary>0 = Por conta do emitente | 1 = Por conta do destinatário | 2 = Por conta de terceiros | 9 = Sem frete</summary>
    public required string ModalidadeFrete { get; init; }

    public TransportadoraRequest? Transportadora { get; init; }
    public VeiculoRequest? Veiculo { get; init; }
    public List<VolumeRequest>? Volumes { get; init; }
}

public sealed record TransportadoraRequest
{
    public string? Cnpj { get; init; }
    public string? Cpf { get; init; }
    public string? RazaoSocial { get; init; }
    public string? InscricaoEstadual { get; init; }
    public string? Endereco { get; init; }
    public string? Municipio { get; init; }
    public string? Uf { get; init; }
}

public sealed record VeiculoRequest
{
    public string? Placa { get; init; }
    public string? Uf { get; init; }
    public string? Rntc { get; init; }
}

public sealed record VolumeRequest
{
    public decimal? Quantidade { get; init; }
    public string? Especie { get; init; }
    public string? Marca { get; init; }
    public string? Numeracao { get; init; }
    public decimal? PesoBruto { get; init; }
    public decimal? PesoLiquido { get; init; }
}

// ─────────────────────────────────────────────
// Pagamento
// ─────────────────────────────────────────────
public sealed record PagamentoRequest
{
    /// <summary>
    /// 01=Dinheiro 02=Cheque 03=Cartão de Crédito 04=Cartão de Débito
    /// 05=Crédito Loja 10=Vale Alimentação 11=Vale Refeição 12=Vale Presente
    /// 13=Vale Combustível 14=Duplicata Mercantil 15=Boleto Bancário 90=Sem pagamento 99=Outros
    /// </summary>
    public required string MeioPagamento { get; init; }
    public required decimal Valor { get; init; }

    public CartaoRequest? Cartao { get; init; }
}

public sealed record CartaoRequest
{
    public string? Tpef { get; init; }
    public string? CnpjCredenciadora { get; init; }
    public string? BandeiraOperadora { get; init; }
    public string? NumeroAutorizacao { get; init; }
}

// ─────────────────────────────────────────────
// Eventos e inutilização de NF-e
// ─────────────────────────────────────────────
public sealed record CancelarNfeRequest
{
    /// <summary>1 = Produção | 2 = Homologação</summary>
    public required string Ambiente { get; init; }
    public required string Uf { get; init; }
    public required string ChaveAcesso { get; init; }
    public required string CnpjEmitente { get; init; }
    public required string ProtocoloAutorizacao { get; init; }
    public required string Justificativa { get; init; }
}

public sealed record CartaCorrecaoRequest
{
    /// <summary>1 = Produção | 2 = Homologação</summary>
    public required string Ambiente { get; init; }
    public required string Uf { get; init; }
    public required string ChaveAcesso { get; init; }
    public required string CnpjEmitente { get; init; }
    public int SequenciaEvento { get; init; } = 1;
    public required string Correcao { get; init; }
}

public sealed record InutilizarNumeracaoRequest
{
    /// <summary>1 = Produção | 2 = Homologação</summary>
    public required string Ambiente { get; init; }
    public required string Uf { get; init; }
    public required string CnpjEmitente { get; init; }
    public required string Ano { get; init; }
    public string Modelo { get; init; } = "55";
    public required string Serie { get; init; }
    public required long NumeroInicial { get; init; }
    public required long NumeroFinal { get; init; }
    public required string Justificativa { get; init; }
}
