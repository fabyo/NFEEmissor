using System.Text;
using System.Xml;
using Nfe.Shared;

namespace Nfe.Core;

/// <summary>
/// Constrói o XML da NF-e modelo 55 conforme o schema NF-e 4.00
/// (PL_009i2 / NT 2021.004 e posteriores).
///
/// A ORDEM DOS ELEMENTOS É OBRIGATÓRIA — o SEFAZ valida contra XSD
/// e qualquer elemento fora de posição gera rejeição 591.
/// </summary>
public interface INfeXmlBuilder
{
    Task<Result<XmlNfeResult>> BuildAsync(EmitirNfeRequest request, CancellationToken ct = default);
}

public sealed record XmlNfeResult
{
    public required string XmlNfe { get; init; }
    public required string ChaveAcesso { get; init; }
    public required string CodigoNumerico { get; init; }
}

public sealed class NfeXmlBuilder : INfeXmlBuilder
{
    // Namespace padrão NF-e 4.00
    private const string NsNfe = "http://www.portalfiscal.inf.br/nfe";

    // Modelo NF-e
    private const string Modelo = "55";

    public Task<Result<XmlNfeResult>> BuildAsync(EmitirNfeRequest req, CancellationToken ct = default)
    {
        try
        {
            var validation = NfeRequestRules.NormalizeAndValidate(req);
            if (!validation.IsSuccess)
            {
                return Task.FromResult(Result<XmlNfeResult>.Failure(
                    validation.ErrorCode!,
                    validation.ErrorMessage!));
            }

            req = validation.Value!;
            var dataEmissao = DateTime.Now;
            var cUf = SefazCodigosHelper.ObterCodigoUf(req.Emitente.Endereco.Uf);

            // Calcular chave de acesso
            var chaveResult = ChaveAcessoCalculator.Calcular(
                cUf: cUf,
                dataEmissao: dataEmissao,
                cnpjEmitente: req.Emitente.Cnpj,
                modelo: int.Parse(Modelo),
                serie: req.Serie,
                numeroNfe: req.NumeroNfe,
                tipoEmissao: int.Parse(req.TipoEmissao)
            );

            // Calcular totais
            var totais = TotaisCalculator.Calcular(req.Produtos);

            // ── Construção do XML ──────────────────────────────────────────
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false), // UTF-8 sem BOM
                Indent = false,                     // SEFAZ não exige indentação; sem indentação = menor tamanho
                OmitXmlDeclaration = false,
            };

            using var ms = new MemoryStream();
            using var writer = XmlWriter.Create(ms, settings);

            // <?xml version="1.0" encoding="UTF-8"?>
            writer.WriteStartDocument();

            // <nfeProc> NÃO envolve o XML antes de autorizar.
            // O que se envia ao SEFAZ é o <NFe> dentro de <enviNFe>.
            // Aqui geramos apenas o <NFe>.
            writer.WriteStartElement("NFe", NsNfe);

            EscreverInfNfe(writer, req, chaveResult, dataEmissao, totais, cUf);

            writer.WriteEndElement(); // </NFe>
            writer.WriteEndDocument();
            writer.Flush();

            var xml = Encoding.UTF8.GetString(ms.ToArray());

            return Task.FromResult(Result<XmlNfeResult>.Success(new XmlNfeResult
            {
                XmlNfe = xml,
                ChaveAcesso = chaveResult.Chave,
                CodigoNumerico = chaveResult.CodigoNumerico,
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                Result<XmlNfeResult>.Failure("XML_BUILD_FALHOU", ex.Message));
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // infNFe — elemento principal (Id = "NFe" + chave 44 dígitos)
    // ─────────────────────────────────────────────────────────────────────
    private void EscreverInfNfe(
        XmlWriter w,
        EmitirNfeRequest req,
        ChaveAcessoResult chave,
        DateTime dataEmissao,
        NfeTotais totais,
        int cUf)
    {
        w.WriteStartElement("infNFe");
        w.WriteAttributeString("Id", $"NFe{chave.Chave}");
        w.WriteAttributeString("versao", "4.00");

        EscreverIde(w, req, chave, dataEmissao, cUf);
        EscreverEmitente(w, req.Emitente);
        EscreverAvulsa(w);          // omitido — emissão normal
        EscreverDestinatario(w, req.Destinatario);
        EscreverRetirada(w);        // opcional — omitido
        EscreverEntrega(w);         // opcional — omitido

        var numItem = 1;
        foreach (var prod in req.Produtos)
            EscreverDetalhe(w, prod, numItem++);

        EscreverTotais(w, totais);
        EscreverTransporte(w, req.Transporte);
        EscreverCobranca(w);        // opcional — omitido
        EscreverPagamento(w, req.Pagamentos);
        EscreverInfAdic(w, req);

        w.WriteEndElement(); // </infNFe>
    }

    // ─────────────────────────────────────────────────────────────────────
    // ide — Identificação da NF-e
    // ─────────────────────────────────────────────────────────────────────
    private static void EscreverIde(
        XmlWriter w, EmitirNfeRequest req, ChaveAcessoResult chave,
        DateTime dataEmissao, int cUf)
    {
        w.WriteStartElement("ide");

        w.WriteElementString("cUF", cUf.ToString());
        w.WriteElementString("cNF", chave.CodigoNumerico);
        w.WriteElementString("natOp", req.NaturezaOperacao.Truncar(60));
        w.WriteElementString("mod", Modelo);
        w.WriteElementString("serie", int.Parse(req.Serie).ToString());
        w.WriteElementString("nNF", req.NumeroNfe.ToString());
        w.WriteElementString("dhEmi", dataEmissao.ToNfeDateTime());
        // dhSaiEnt — data/hora de saída/entrada; opcional; omitida aqui
        w.WriteElementString("tpNF", req.TipoOperacao);           // 0=Entrada | 1=Saída  
        w.WriteElementString("idDest", ObterIdDest(req));          // 1=interna | 2=interestadual | 3=exterior
        w.WriteElementString("cMunFG", req.Emitente.Endereco.CodigoMunicipio);
        w.WriteElementString("tpImp", req.FormatoImpressaoDanfe);
        w.WriteElementString("tpEmis", req.TipoEmissao);
        w.WriteElementString("cDV", chave.DigitoVerificador.ToString());
        w.WriteElementString("tpAmb", req.AmbienteEmissao);
        w.WriteElementString("finNFe", req.FinalidadeEmissao);
        w.WriteElementString("indFinal", req.ConsumidorFinal);
        w.WriteElementString("indPres", req.IndicadorPresencaComprador);
        w.WriteElementString("indIntermed", "0"); // 0=Sem intermediário (padrão)
        w.WriteElementString("procEmi", "0");    // 0=Emissão de aplicativo do contribuinte
        w.WriteElementString("verProc", NfeVersion.Current.Truncar(20));

        foreach (var refNFe in req.NotasReferenciadas.Where(static x => !string.IsNullOrWhiteSpace(x)))
        {
            w.WriteStartElement("NFref");
            w.WriteElementString("refNFe", refNFe.OnlyAlphaNumericUpper());
            w.WriteEndElement();
        }

        w.WriteEndElement(); // </ide>
    }

    private static void EscreverEmitente(XmlWriter w, EmitenteRequest emit)
    {
        w.WriteStartElement("emit");
        w.WriteElementString("CNPJ", emit.Cnpj.OnlyAlphaNumericUpper());
        w.WriteElementString("xNome", emit.RazaoSocial.Truncar(60));
        if (!string.IsNullOrWhiteSpace(emit.NomeFantasia))
            w.WriteElementString("xFant", emit.NomeFantasia.Truncar(60));

        EscreverEnderecoEmitente(w, emit.Endereco);

        w.WriteElementString("IE", emit.InscricaoEstadual.OnlyDigits());
        if (!string.IsNullOrWhiteSpace(emit.InscricaoEstadualSt))
            w.WriteElementString("IEST", emit.InscricaoEstadualSt.OnlyDigits());
        if (!string.IsNullOrWhiteSpace(emit.InscricaoMunicipal))
            w.WriteElementString("IM", emit.InscricaoMunicipal.OnlyDigits());
        w.WriteElementString("CRT", emit.CodigoRegimeTributario);

        w.WriteEndElement(); // </emit>
    }

    private static void EscreverEnderecoEmitente(XmlWriter w, EnderecoRequest end)
    {
        w.WriteStartElement("enderEmit");
        w.WriteElementString("xLgr", end.Logradouro.Truncar(60));
        w.WriteElementString("nro", end.Numero.Truncar(60));
        if (!string.IsNullOrWhiteSpace(end.Complemento))
            w.WriteElementString("xCpl", end.Complemento.Truncar(60));
        w.WriteElementString("xBairro", end.Bairro.Truncar(60));
        w.WriteElementString("cMun", end.CodigoMunicipio);
        w.WriteElementString("xMun", end.NomeMunicipio.Truncar(60));
        w.WriteElementString("UF", end.Uf.ToUpper());
        w.WriteElementString("CEP", end.Cep.OnlyDigits());
        w.WriteElementString("cPais", end.CodigoPais);
        w.WriteElementString("xPais", end.NomePais.Truncar(60));
        if (!string.IsNullOrWhiteSpace(end.Telefone))
            w.WriteElementString("fone", end.Telefone.OnlyDigits());
        w.WriteEndElement(); // </enderEmit>
    }

    private static void EscreverAvulsa(XmlWriter w)
    {
        // Omitida
    }

    private static void EscreverDestinatario(XmlWriter w, DestinatarioRequest dest)
    {
        w.WriteStartElement("dest");

        if (!string.IsNullOrWhiteSpace(dest.Cnpj))
            w.WriteElementString("CNPJ", dest.Cnpj.OnlyAlphaNumericUpper());
        else if (!string.IsNullOrWhiteSpace(dest.Cpf))
            w.WriteElementString("CPF", dest.Cpf.OnlyDigits());
        else if (!string.IsNullOrWhiteSpace(dest.IdEstrangeiro))
            w.WriteElementString("idEstrangeiro", dest.IdEstrangeiro.Truncar(20));

        w.WriteElementString("xNome", dest.NomeRazaoSocial.Truncar(60));

        EscreverEnderecoDestinatario(w, dest.Endereco);

        w.WriteElementString("indIEDest", dest.IndicadorIe);

        if (dest.IndicadorIe == "1" && !string.IsNullOrWhiteSpace(dest.InscricaoEstadual))
            w.WriteElementString("IE", dest.InscricaoEstadual.OnlyDigits());

        if (!string.IsNullOrWhiteSpace(dest.Email))
            w.WriteElementString("email", dest.Email.Truncar(60));

        w.WriteEndElement(); // </dest>
    }

    private static void EscreverEnderecoDestinatario(XmlWriter w, EnderecoRequest end)
    {
        w.WriteStartElement("enderDest");
        w.WriteElementString("xLgr", end.Logradouro.Truncar(60));
        w.WriteElementString("nro", end.Numero.Truncar(60));
        if (!string.IsNullOrWhiteSpace(end.Complemento))
            w.WriteElementString("xCpl", end.Complemento.Truncar(60));
        w.WriteElementString("xBairro", end.Bairro.Truncar(60));
        w.WriteElementString("cMun", end.CodigoMunicipio);
        w.WriteElementString("xMun", end.NomeMunicipio.Truncar(60));
        w.WriteElementString("UF", end.Uf.ToUpper());
        w.WriteElementString("CEP", end.Cep.OnlyDigits());
        w.WriteElementString("cPais", end.CodigoPais);
        w.WriteElementString("xPais", end.NomePais.Truncar(60));
        if (!string.IsNullOrWhiteSpace(end.Telefone))
            w.WriteElementString("fone", end.Telefone.OnlyDigits());
        w.WriteEndElement(); // </enderDest>
    }

    private static void EscreverRetirada(XmlWriter w) { }
    private static void EscreverEntrega(XmlWriter w) { }

    // ─────────────────────────────────────────────────────────────────────
    // det — Detalhamento de Produtos e Serviços
    // ─────────────────────────────────────────────────────────────────────
    private static void EscreverDetalhe(XmlWriter w, ProdutoRequest p, int numItem)
    {
        w.WriteStartElement("det");
        w.WriteAttributeString("nItem", numItem.ToString());

        EscreverProduto(w, p);
        EscreverImposto(w, p.Impostos);

        w.WriteEndElement(); // </det>
    }

    private static void EscreverProduto(XmlWriter w, ProdutoRequest p)
    {
        w.WriteStartElement("prod");
        w.WriteElementString("cProd", p.CodigoProduto.Truncar(60));

        w.WriteElementString("cEAN", string.IsNullOrWhiteSpace(p.CodigoEan) ? "SEM GTIN" : p.CodigoEan);
        if (!string.IsNullOrWhiteSpace(p.CodigoBarra)) w.WriteElementString("cBarra", p.CodigoBarra);
        w.WriteElementString("xProd", p.Descricao.Truncar(120));
        w.WriteElementString("NCM", p.NcmSh.OnlyDigits());
        if (!string.IsNullOrWhiteSpace(p.CodigoBeneficioFiscal)) w.WriteElementString("cBenef", p.CodigoBeneficioFiscal);
        if (!string.IsNullOrWhiteSpace(p.Cest)) w.WriteElementString("CEST", p.Cest.OnlyDigits());
        if (!string.IsNullOrWhiteSpace(p.CodigoExTipi)) w.WriteElementString("EXTIPI", p.CodigoExTipi);

        w.WriteElementString("CFOP", p.Cfop.OnlyDigits());
        w.WriteElementString("uCom", p.UnidadeComercial.Truncar(6));
        w.WriteElementString("qCom", p.QuantidadeComercial.ToNfeDecimal(4));
        w.WriteElementString("vUnCom", p.ValorUnitarioComercial.ToNfeDecimalFlex(10));
        w.WriteElementString("vProd", p.ValorBruto.ToNfeDecimal(2));

        w.WriteElementString("cEANTrib", string.IsNullOrWhiteSpace(p.CodigoEanTributavel) ? "SEM GTIN" : p.CodigoEanTributavel);
        w.WriteElementString("uTrib", string.IsNullOrWhiteSpace(p.UnidadeTributavel) ? p.UnidadeComercial.Truncar(6) : p.UnidadeTributavel.Truncar(6));
        w.WriteElementString("qTrib", (p.QuantidadeTributavel == 0 ? p.QuantidadeComercial : p.QuantidadeTributavel).ToNfeDecimal(4));
        w.WriteElementString("vUnTrib", (p.ValorUnitarioTributavel == 0 ? p.ValorUnitarioComercial : p.ValorUnitarioTributavel).ToNfeDecimalFlex(10));

        if (p.ValorFrete > 0) w.WriteElementString("vFrete", p.ValorFrete.ToNfeDecimal(2));
        if (p.ValorSeguro > 0) w.WriteElementString("vSeg", p.ValorSeguro.ToNfeDecimal(2));
        if (p.ValorDesconto > 0) w.WriteElementString("vDesc", p.ValorDesconto.ToNfeDecimal(2));
        if (p.ValorOutrasDespesas > 0) w.WriteElementString("vOutro", p.ValorOutrasDespesas.ToNfeDecimal(2));

        w.WriteElementString("indTot", p.IndicadorComposicaoTotal);
        w.WriteEndElement(); // </prod>
    }

    // ─────────────────────────────────────────────────────────────────────
    // imposto — Grupo de Impostos do Item
    // ─────────────────────────────────────────────────────────────────────
    private static void EscreverImposto(XmlWriter w, ImpostosRequest imp)
    {
        w.WriteStartElement("imposto");

        // Regra SEFAZ: ordem obrigatória dos tributos: ICMS, IPI, II, ISSQN, PIS, COFINS...
        if (imp.Icms != null) EscreverIcms(w, imp.Icms);
        if (imp.Ipi != null) EscreverIpi(w, imp.Ipi);
        if (imp.Pis != null) EscreverPis(w, imp.Pis);
        if (imp.Cofins != null) EscreverCofins(w, imp.Cofins);
        if (imp.IbsCbs != null) EscreverIbsCbs(w, imp.IbsCbs);

        w.WriteEndElement(); // </imposto>
    }

    private static void EscreverIcms(XmlWriter w, IcmsRequest icms)
    {
        w.WriteStartElement("ICMS");

        var cst = icms.Cst;

        // Se for Simples Nacional (CSOSN 101, 102, 201, 500, etc.)
        var isSimples = cst is "101" or "102" or "103" or "201" or "202" or "203" or "300" or "400" or "500" or "900";
        w.WriteStartElement(isSimples ? $"ICMSSN{cst}" : ObterGrupoIcms(cst));

        w.WriteElementString("orig", icms.Origem);

        if (isSimples)
        {
            w.WriteElementString("CSOSN", cst);
            if (cst is "101" or "201" or "900")
            {
                w.WriteElementString("pCredSN", icms.Aliquota.ToNfeDecimal(2));
                w.WriteElementString("vCredICMSSN", icms.Valor.ToNfeDecimal(2));
            }
        }
        else
        {
            w.WriteElementString("CST", cst);

            // Estrutura simplificada baseada no CST
            if (cst is "00" or "10" or "20" or "70" or "90")
            {
                w.WriteElementString("modBC", icms.ModalidadeBaseCalculo);
                w.WriteElementString("vBC", icms.BaseCalculo.ToNfeDecimal(2));
                w.WriteElementString("pICMS", icms.Aliquota.ToNfeDecimal(2));
                w.WriteElementString("vICMS", icms.Valor.ToNfeDecimal(2));
            }
            else if (cst is "40" or "41" or "50")
            {
                if (icms.ValorDesonerado > 0)
                {
                    w.WriteElementString("vICMSDeson", icms.ValorDesonerado.ToNfeDecimal(2));
                    w.WriteElementString("motDesICMS", icms.MotivoDesoneracaoSt ?? "9");
                }
            }
        }

        w.WriteEndElement(); // Fim do grupo do CST específico
        w.WriteEndElement(); // </ICMS>
    }

    private static void EscreverIpi(XmlWriter w, IpiRequest ipi)
    {
        w.WriteStartElement("IPI");
        w.WriteElementString("cEnq", ipi.CodigoEnquadramentoLegal);

        var grupo = ipi.Cst is "00" or "49" or "50" or "99" ? "IPITrib" : "IPINT";
        w.WriteStartElement(grupo);
        w.WriteElementString("CST", ipi.Cst);
        if (grupo == "IPITrib")
        {
            w.WriteElementString("vBC", ipi.BaseCalculo.ToNfeDecimal(2));
            w.WriteElementString("pIPI", ipi.Aliquota.ToNfeDecimal(2));
            w.WriteElementString("vIPI", ipi.Valor.ToNfeDecimal(2));
        }
        w.WriteEndElement();

        w.WriteEndElement(); // </IPI>
    }

    private static string ObterGrupoIcms(string cst) => cst switch
    {
        "40" or "41" or "50" => "ICMS40",
        _ => $"ICMS{cst}"
    };

    private static void EscreverPis(XmlWriter w, PisRequest pis)
    {
        w.WriteStartElement("PIS");

        var cst = pis.Cst;
        // PISAliq (01, 02), PISQtde (03), PISNT (04..09), PISOutr (49..99)
        var grupo = cst is "01" or "02" ? "PISAliq" : (cst is "04" or "05" or "06" or "07" or "08" or "09" ? "PISNT" : "PISOutr");

        w.WriteStartElement(grupo);
        w.WriteElementString("CST", cst);

        if (grupo == "PISAliq" || grupo == "PISOutr")
        {
            w.WriteElementString("vBC", pis.BaseCalculo.ToNfeDecimal(2));
            w.WriteElementString("pPIS", pis.Aliquota.ToNfeDecimal(2));
            w.WriteElementString("vPIS", pis.Valor.ToNfeDecimal(2));
        }

        w.WriteEndElement();
        w.WriteEndElement(); // </PIS>
    }

    private static void EscreverCofins(XmlWriter w, CofinsRequest cofins)
    {
        w.WriteStartElement("COFINS");

        var cst = cofins.Cst;
        var grupo = cst is "01" or "02" ? "COFINSAliq" : (cst is "04" or "05" or "06" or "07" or "08" or "09" ? "COFINSNT" : "COFINSOutr");

        w.WriteStartElement(grupo);
        w.WriteElementString("CST", cst);

        if (grupo == "COFINSAliq" || grupo == "COFINSOutr")
        {
            w.WriteElementString("vBC", cofins.BaseCalculo.ToNfeDecimal(2));
            w.WriteElementString("pCOFINS", cofins.Aliquota.ToNfeDecimal(2));
            w.WriteElementString("vCOFINS", cofins.Valor.ToNfeDecimal(2));
        }

        w.WriteEndElement();
        w.WriteEndElement(); // </COFINS>
    }

    private static void EscreverIbsCbs(XmlWriter w, IbsCbsRequest ibsCbs)
    {
        w.WriteStartElement("IBSCBS");
        w.WriteElementString("CST", ibsCbs.Cst);
        w.WriteElementString("cClassTrib", ibsCbs.CodigoClassificacaoTributaria);

        if (ibsCbs.BaseCalculo > 0 || ibsCbs.IbsUf != null || ibsCbs.IbsMunicipio != null || ibsCbs.Cbs != null)
        {
            w.WriteStartElement("gIBSCBS");
            w.WriteElementString("vBC", ibsCbs.BaseCalculo.ToNfeDecimal(2));
            if (ibsCbs.IbsUf != null) EscreverTributoReforma(w, "gIBSUF", "pIBSUF", "vIBSUF", ibsCbs.IbsUf);
            if (ibsCbs.IbsMunicipio != null) EscreverTributoReforma(w, "gIBSMun", "pIBSMun", "vIBSMun", ibsCbs.IbsMunicipio);
            if (ibsCbs.Cbs != null) EscreverTributoReforma(w, "gCBS", "pCBS", "vCBS", ibsCbs.Cbs);
            w.WriteEndElement();
        }

        w.WriteEndElement(); // </IBSCBS>
    }

    private static void EscreverTributoReforma(XmlWriter w, string grupo, string tagAliquota, string tagValor, TributoReformaRequest tributo)
    {
        w.WriteStartElement(grupo);
        w.WriteElementString(tagAliquota, tributo.Aliquota.ToNfeDecimal(4));

        if (tributo.PercentualDiferimento > 0 || tributo.ValorDiferimento > 0)
        {
            w.WriteStartElement("gDif");
            w.WriteElementString("pDif", tributo.PercentualDiferimento.ToNfeDecimal(4));
            w.WriteElementString("vDif", tributo.ValorDiferimento.ToNfeDecimal(2));
            w.WriteEndElement();
        }

        if (tributo.ValorDevolucaoTributo > 0)
        {
            w.WriteStartElement("gDevTrib");
            w.WriteElementString("vDevTrib", tributo.ValorDevolucaoTributo.ToNfeDecimal(2));
            w.WriteEndElement();
        }

        if (tributo.PercentualReducaoAliquota > 0 || tributo.AliquotaEfetiva > 0)
        {
            w.WriteStartElement("gRed");
            w.WriteElementString("pRedAliq", tributo.PercentualReducaoAliquota.ToNfeDecimal(4));
            w.WriteElementString("pAliqEfet", tributo.AliquotaEfetiva.ToNfeDecimal(4));
            w.WriteEndElement();
        }

        w.WriteElementString(tagValor, tributo.Valor.ToNfeDecimal(2));
        w.WriteEndElement();
    }

    // ─────────────────────────────────────────────────────────────────────
    // total — Consolidação dos Totais da NF-e
    // ─────────────────────────────────────────────────────────────────────
    private static void EscreverTotais(XmlWriter w, NfeTotais t)
    {
        w.WriteStartElement("total");
        w.WriteStartElement("ICMSTot");

        w.WriteElementString("vBC", t.BaseCalculoIcms.ToNfeDecimal(2));
        w.WriteElementString("vICMS", t.ValorIcms.ToNfeDecimal(2));
        w.WriteElementString("vICMSDeson", t.ValorIcmsDesonerado.ToNfeDecimal(2));
        w.WriteElementString("vFCP", t.ValorFcp.ToNfeDecimal(2));
        w.WriteElementString("vBCST", t.BaseCalculoIcmsSt.ToNfeDecimal(2));
        w.WriteElementString("vST", t.ValorIcmsSt.ToNfeDecimal(2));
        w.WriteElementString("vFCPST", t.ValorFcpSt.ToNfeDecimal(2));
        w.WriteElementString("vFCPSTRet", t.ValorFcpStRetido.ToNfeDecimal(2));
        w.WriteElementString("vProd", t.ValorProdutos.ToNfeDecimal(2));
        w.WriteElementString("vFrete", t.ValorFrete.ToNfeDecimal(2));
        w.WriteElementString("vSeg", t.ValorSeguro.ToNfeDecimal(2));
        w.WriteElementString("vDesc", t.ValorDesconto.ToNfeDecimal(2));
        w.WriteElementString("vII", "0.00");
        w.WriteElementString("vIPI", t.ValorIpi.ToNfeDecimal(2));
        w.WriteElementString("vIPIDevol", t.ValorIpiDevolvido.ToNfeDecimal(2));
        w.WriteElementString("vPIS", t.ValorPis.ToNfeDecimal(2));
        w.WriteElementString("vCOFINS", t.ValorCofins.ToNfeDecimal(2));
        w.WriteElementString("vOutro", t.ValorOutrasDespesas.ToNfeDecimal(2));
        w.WriteElementString("vNF", t.ValorNfe.ToNfeDecimal(2));

        w.WriteEndElement(); // </ICMSTot>
        if (t.TemIbsCbs) EscreverTotaisIbsCbs(w, t);
        w.WriteEndElement(); // </total>
    }

    private static void EscreverTotaisIbsCbs(XmlWriter w, NfeTotais t)
    {
        w.WriteStartElement("IBSCBSTot");
        w.WriteElementString("vBCIBSCBS", t.BaseCalculoIbsCbs.ToNfeDecimal(2));

        w.WriteStartElement("gIBS");
        w.WriteStartElement("gIBSUF");
        w.WriteElementString("vDif", t.ValorDiferimentoIbsUf.ToNfeDecimal(2));
        w.WriteElementString("vDevTrib", t.ValorDevolucaoTributoIbsUf.ToNfeDecimal(2));
        w.WriteElementString("vIBSUF", t.ValorIbsUf.ToNfeDecimal(2));
        w.WriteEndElement();

        w.WriteStartElement("gIBSMun");
        w.WriteElementString("vDif", t.ValorDiferimentoIbsMunicipio.ToNfeDecimal(2));
        w.WriteElementString("vDevTrib", t.ValorDevolucaoTributoIbsMunicipio.ToNfeDecimal(2));
        w.WriteElementString("vIBSMun", t.ValorIbsMunicipio.ToNfeDecimal(2));
        w.WriteEndElement();

        w.WriteElementString("vIBS", (t.ValorIbsUf + t.ValorIbsMunicipio).ToNfeDecimal(2));
        w.WriteEndElement();

        w.WriteStartElement("gCBS");
        w.WriteElementString("vDif", t.ValorDiferimentoCbs.ToNfeDecimal(2));
        w.WriteElementString("vDevTrib", t.ValorDevolucaoTributoCbs.ToNfeDecimal(2));
        w.WriteElementString("vCBS", t.ValorCbsReforma.ToNfeDecimal(2));
        w.WriteEndElement();

        w.WriteEndElement(); // </IBSCBSTot>
    }

    // ─────────────────────────────────────────────────────────────────────
    // transp — Informações de Transporte
    // ─────────────────────────────────────────────────────────────────────
    private static void EscreverTransporte(XmlWriter w, TransporteRequest t)
    {
        w.WriteStartElement("transp");
        w.WriteElementString("modFrete", t.ModalidadeFrete);

        if (t.Transportadora != null)
        {
            var tr = t.Transportadora;
            w.WriteStartElement("transporta");
            if (!string.IsNullOrWhiteSpace(tr.Cnpj)) w.WriteElementString("CNPJ", tr.Cnpj.OnlyAlphaNumericUpper());
            else if (!string.IsNullOrWhiteSpace(tr.Cpf)) w.WriteElementString("CPF", tr.Cpf.OnlyDigits());
            if (!string.IsNullOrWhiteSpace(tr.RazaoSocial)) w.WriteElementString("xNome", tr.RazaoSocial.Truncar(60));
            if (!string.IsNullOrWhiteSpace(tr.InscricaoEstadual)) w.WriteElementString("IE", tr.InscricaoEstadual.OnlyDigits());
            if (!string.IsNullOrWhiteSpace(tr.Endereco)) w.WriteElementString("xEnder", tr.Endereco.Truncar(60));
            if (!string.IsNullOrWhiteSpace(tr.Municipio)) w.WriteElementString("xMun", tr.Municipio.Truncar(60));
            if (!string.IsNullOrWhiteSpace(tr.Uf)) w.WriteElementString("UF", tr.Uf.ToUpper());
            w.WriteEndElement();
        }

        w.WriteEndElement(); // </transp>
    }

    private static void EscreverCobranca(XmlWriter w) { }

    // ─────────────────────────────────────────────────────────────────────
    // pag — Formas de pagamento (obrigatório na NF-e 4.00)
    // ─────────────────────────────────────────────────────────────────────
    private static void EscreverPagamento(XmlWriter w, List<PagamentoRequest> pagamentos)
    {
        w.WriteStartElement("pag");

        foreach (var pg in pagamentos)
        {
            w.WriteStartElement("detPag");
            w.WriteElementString("tPag", pg.MeioPagamento);
            w.WriteElementString("vPag", pg.Valor.ToNfeDecimal(2));

            if (pg.Cartao is { } c)
            {
                w.WriteStartElement("card");
                if (!string.IsNullOrWhiteSpace(c.Tpef)) w.WriteElementString("tpIntegra", c.Tpef);
                if (!string.IsNullOrWhiteSpace(c.CnpjCredenciadora)) w.WriteElementString("CNPJ", c.CnpjCredenciadora.OnlyAlphaNumericUpper());
                if (!string.IsNullOrWhiteSpace(c.BandeiraOperadora)) w.WriteElementString("tBand", c.BandeiraOperadora);
                if (!string.IsNullOrWhiteSpace(c.NumeroAutorizacao)) w.WriteElementString("cAut", c.NumeroAutorizacao);
                w.WriteEndElement();
            }

            w.WriteEndElement(); // </detPag>
        }

        w.WriteEndElement(); // </pag>
    }

    // ─────────────────────────────────────────────────────────────────────
    // infAdic — Informações adicionais
    // ─────────────────────────────────────────────────────────────────────
    private static void EscreverInfAdic(XmlWriter w, EmitirNfeRequest req)
    {
        var temFisco = !string.IsNullOrWhiteSpace(req.InformacoesAdicionaisFisco);
        var temAdic = !string.IsNullOrWhiteSpace(req.InformacoesAdicionais);

        if (!temFisco && !temAdic) return;

        w.WriteStartElement("infAdic");
        if (temFisco) w.WriteElementString("infAdFisco", req.InformacoesAdicionaisFisco!.Truncar(2000));
        if (temAdic) w.WriteElementString("infCpl", req.InformacoesAdicionais!.Truncar(5000));
        w.WriteEndElement(); // </infAdic>
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// idDest: 1=Operação interna (mesma UF) | 2=Operação interestadual | 3=Com o exterior
    /// </summary>
    private static string ObterIdDest(EmitirNfeRequest req)
    {
        var ufEmit = req.Emitente.Endereco.Uf.ToUpper();
        var ufDest = req.Destinatario.Endereco.Uf.ToUpper();

        if (ufDest == "EX") return "3"; // exterior
        return ufEmit == ufDest ? "1" : "2";
    }
}

// ─────────────────────────────────────────────────────────────────────────
// Extension methods de formatação para XML NF-e
// ─────────────────────────────────────────────────────────────────────────
public static class NfeFormatExtensions
{
    /// <summary>
    /// Formata decimal para o padrão NF-e: ponto como separador, sem agrupamento,
    /// número fixo de casas decimais conforme a tag.
    /// </summary>
    public static string ToNfeDecimal(this decimal value, int casas) =>
        value.ToString($"F{casas}", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Formata decimal com limite de casas, removendo zeros à direita.
    /// Útil para valores unitários que aceitam precisão maior, mas não exigem casas fixas.
    /// </summary>
    public static string ToNfeDecimalFlex(this decimal value, int maxCasas)
    {
        var rounded = decimal.Round(value, maxCasas);
        return rounded.ToString($"0.{new string('#', maxCasas)}", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Data/hora no formato ISO 8601 com offset local: yyyy-MM-ddTHH:mm:sszzz
    /// O SEFAZ exige offset explicito; no container fixamos Sao Paulo para emissoes SP.
    /// </summary>
    public static string ToNfeDateTime(this DateTime dt)
    {
        var timeZone = ObterTimeZoneSaoPaulo();
        var origem = new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt));
        var local = TimeZoneInfo.ConvertTime(origem, timeZone);
        return local.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static TimeZoneInfo ObterTimeZoneSaoPaulo()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
        }
    }

    /// <summary>Remove todos os caracteres não-numéricos.</summary>
    public static string OnlyDigits(this string s) =>
        new(s.Where(char.IsDigit).ToArray());

    /// <summary>Remove pontuação e mantém apenas letras/números em maiúsculo.</summary>
    public static string OnlyAlphaNumericUpper(this string s) =>
        new(s.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>Trunca string sem lançar exceção.</summary>
    public static string Truncar(this string s, int maxLength) =>
        s.Length <= maxLength ? s : s[..maxLength];
}
