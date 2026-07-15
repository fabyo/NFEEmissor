using System.Xml;
using System.Reflection;
using NFEDanfe;
using Nfe.Core;
using Nfe.Shared;

namespace Nfe.UnitTests;

public sealed class NfeOfflineIntegrationTests
{
    [Fact]
    public void NfeProcBuilder_DeveMontarProcNFeSemSefaz()
    {
        var xmlAssinado = CriarXmlNfeAssinadoMinimo();
        var protNFe = CriarProtNFe();

        var procNFe = NfeProcBuilder.Montar(xmlAssinado, protNFe);

        var doc = new XmlDocument();
        doc.LoadXml(procNFe);

        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");

        Assert.Equal("nfeProc", doc.DocumentElement!.LocalName);
        Assert.NotNull(doc.SelectSingleNode("/nfe:nfeProc/nfe:NFe", ns));
        Assert.NotNull(doc.SelectSingleNode("/nfe:nfeProc/nfe:protNFe", ns));
        Assert.Equal("100", doc.SelectSingleNode("//nfe:protNFe/nfe:infProt/nfe:cStat", ns)?.InnerText);
    }

    [Fact]
    public void Danfe_DeveGerarPdfDeProcNFeOfflineSemSefaz()
    {
        var procNFe = NfeProcBuilder.Montar(CriarXmlNfeAssinadoMinimo(), CriarProtNFe());

        using var pdfStream = new MemoryStream();
        DanfeGenerator.GenerateFromXmlContent(procNFe, pdfStream);

        var pdfBytes = pdfStream.ToArray();
        Assert.True(pdfBytes.Length > 1000);
        Assert.Equal("%PDF"u8.ToArray(), pdfBytes[..4]);
    }

    [Fact]
    public void SefazService_DeveRetornarErroEstruturadoSemChamarSefaz()
    {
        var method = typeof(NfeSefazService).GetMethod("ParsearRetornoAutorizacao", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = (Result<SefazAutorizacaoResult>)method!.Invoke(null, [CriarRetornoSefazRejeitado(), CriarXmlNfeAssinadoMinimo()])!;

        Assert.False(result.IsSuccess);
        Assert.Equal("SEFAZ_267", result.ErrorCode);
        var erro = Assert.IsType<SefazErroException>(result.Exception);
        Assert.Equal("267", erro.CStat);
        Assert.Equal("Rejeição: Chave de Acesso referenciada inexistente [nRef: 1]", erro.XMotivo);
        Assert.Equal("35260612345678000195550010000000011000000010", erro.ChaveAcesso);
    }

    [Fact]
    public void EventoService_DeveMontarXmlCancelamento()
    {
        var service = new NfeEventoService(new NfeAssinadorService());

        var result = service.MontarXmlCancelamento(new CancelarNfeRequest
        {
            Ambiente = "2",
            Uf = "SP",
            ChaveAcesso = "35260612345678000195550010000000011000000010",
            CnpjEmitente = "12.345.678/0001-95",
            ProtocoloAutorizacao = "135000000000000",
            Justificativa = "Erro operacional identificado apos autorizacao"
        });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var doc = new XmlDocument();
        doc.LoadXml(result.Value!);

        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");

        Assert.Equal("evento", doc.DocumentElement!.LocalName);
        Assert.Equal("ID1101113526061234567800019555001000000001100000001001", doc.SelectSingleNode("//nfe:infEvento", ns)?.Attributes?["Id"]?.Value);
        Assert.Equal("Cancelamento", doc.SelectSingleNode("//nfe:detEvento/nfe:descEvento", ns)?.InnerText);
        Assert.Equal("135000000000000", doc.SelectSingleNode("//nfe:detEvento/nfe:nProt", ns)?.InnerText);
    }

    [Fact]
    public void EventoService_DeveMontarXmlCartaCorrecao()
    {
        var service = new NfeEventoService(new NfeAssinadorService());

        var result = service.MontarXmlCartaCorrecao(new CartaCorrecaoRequest
        {
            Ambiente = "2",
            Uf = "SP",
            ChaveAcesso = "35260612345678000195550010000000011000000010",
            CnpjEmitente = "12345678000195",
            SequenciaEvento = 2,
            Correcao = "Correção do texto das informações adicionais da nota fiscal"
        });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var doc = new XmlDocument();
        doc.LoadXml(result.Value!);

        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");

        Assert.Equal("ID1101103526061234567800019555001000000001100000001002", doc.SelectSingleNode("//nfe:infEvento", ns)?.Attributes?["Id"]?.Value);
        Assert.Equal("Carta de Correcao", doc.SelectSingleNode("//nfe:detEvento/nfe:descEvento", ns)?.InnerText);
        Assert.Contains("informações adicionais", doc.SelectSingleNode("//nfe:detEvento/nfe:xCorrecao", ns)?.InnerText);
        Assert.NotNull(doc.SelectSingleNode("//nfe:detEvento/nfe:xCondUso", ns));
    }

    [Fact]
    public void EventoService_DeveMontarXmlInutilizacao()
    {
        var service = new NfeEventoService(new NfeAssinadorService());

        var result = service.MontarXmlInutilizacao(new InutilizarNumeracaoRequest
        {
            Ambiente = "2",
            Uf = "SP",
            CnpjEmitente = "12.345.678/0001-95",
            Ano = "2026",
            Serie = "1",
            NumeroInicial = 10,
            NumeroFinal = 12,
            Justificativa = "Quebra de sequência por erro operacional interno"
        });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var doc = new XmlDocument();
        doc.LoadXml(result.Value!);

        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");

        Assert.Equal("inutNFe", doc.DocumentElement!.LocalName);
        Assert.Equal("ID35261234567800019555001000000010000000012", doc.SelectSingleNode("//nfe:infInut", ns)?.Attributes?["Id"]?.Value);
        Assert.Equal("10", doc.SelectSingleNode("//nfe:infInut/nfe:nNFIni", ns)?.InnerText);
        Assert.Equal("12", doc.SelectSingleNode("//nfe:infInut/nfe:nNFFin", ns)?.InnerText);
    }

    [Fact]
    public void EventoService_DeveParsearRetornoEvento()
    {
        var method = typeof(NfeEventoService).GetMethod("ParsearRetornoEvento", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = (Result<NfeEventoResult>)method!.Invoke(null, [
            CriarRetornoEventoAutorizado(),
            CriarXmlEventoAssinadoMinimo(),
            "35260612345678000195550010000000011000000010",
            "110111",
            1
        ])!;

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("135", result.Value!.Status);
        Assert.Equal("135000000000001", result.Value.Protocolo);
        Assert.Contains("<procEventoNFe", result.Value.XmlProcEventoNfe);
    }

    [Fact]
    public void EventoService_DeveParsearRetornoInutilizacao()
    {
        var method = typeof(NfeEventoService).GetMethod("ParsearRetornoInutilizacao", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = (Result<NfeInutilizacaoResult>)method!.Invoke(null, [
            CriarRetornoInutilizacaoAutorizada(),
            "<inutNFe xmlns=\"http://www.portalfiscal.inf.br/nfe\" versao=\"4.00\" />"
        ])!;

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("102", result.Value!.Status);
        Assert.Equal("135000000000002", result.Value.Protocolo);
        Assert.Equal(10, result.Value.NumeroInicial);
        Assert.Equal(12, result.Value.NumeroFinal);
    }

    private static string CriarXmlNfeAssinadoMinimo() => """
        <?xml version="1.0" encoding="utf-8"?>
        <NFe xmlns="http://www.portalfiscal.inf.br/nfe">
          <infNFe Id="NFe35260612345678000195550010000000011000000010" versao="4.00">
            <ide>
              <cUF>35</cUF>
              <cNF>00000001</cNF>
              <natOp>VENDA DE MERCADORIA</natOp>
              <mod>55</mod>
              <serie>1</serie>
              <nNF>1</nNF>
              <dhEmi>2026-06-25T10:00:00-03:00</dhEmi>
              <tpNF>1</tpNF>
              <idDest>1</idDest>
              <cMunFG>3550308</cMunFG>
              <tpImp>1</tpImp>
              <tpEmis>1</tpEmis>
              <cDV>0</cDV>
              <tpAmb>2</tpAmb>
              <finNFe>1</finNFe>
              <indFinal>1</indFinal>
              <indPres>9</indPres>
              <procEmi>0</procEmi>
              <verProc>1.0</verProc>
            </ide>
            <emit>
              <CNPJ>12345678000195</CNPJ>
              <xNome>EMPRESA EMITENTE TESTE LTDA</xNome>
              <enderEmit>
                <xLgr>RUA TESTE</xLgr>
                <nro>100</nro>
                <xBairro>CENTRO</xBairro>
                <cMun>3550308</cMun>
                <xMun>SAO PAULO</xMun>
                <UF>SP</UF>
                <CEP>01001000</CEP>
                <cPais>1058</cPais>
                <xPais>BRASIL</xPais>
              </enderEmit>
              <IE>110042490114</IE>
              <CRT>3</CRT>
            </emit>
            <dest>
              <CNPJ>99999999000191</CNPJ>
              <xNome>NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL</xNome>
              <enderDest>
                <xLgr>AVENIDA CLIENTE</xLgr>
                <nro>200</nro>
                <xBairro>JARDINS</xBairro>
                <cMun>3550308</cMun>
                <xMun>SAO PAULO</xMun>
                <UF>SP</UF>
                <CEP>02002000</CEP>
                <cPais>1058</cPais>
                <xPais>Brasil</xPais>
              </enderDest>
              <indIEDest>9</indIEDest>
            </dest>
            <det nItem="1">
              <prod>
                <cProd>PROD-001</cProd>
                <cEAN>SEM GTIN</cEAN>
                <xProd>PRODUTO TESTE</xProd>
                <NCM>73090090</NCM>
                <CFOP>5102</CFOP>
                <uCom>UN</uCom>
                <qCom>1.0000</qCom>
                <vUnCom>100</vUnCom>
                <vProd>100.00</vProd>
                <cEANTrib>SEM GTIN</cEANTrib>
                <uTrib>UN</uTrib>
                <qTrib>1.0000</qTrib>
                <vUnTrib>100</vUnTrib>
                <indTot>1</indTot>
              </prod>
              <imposto>
                <ICMS><ICMS00><orig>0</orig><CST>00</CST><modBC>3</modBC><vBC>100.00</vBC><pICMS>18.00</pICMS><vICMS>18.00</vICMS></ICMS00></ICMS>
                <PIS><PISAliq><CST>01</CST><vBC>100.00</vBC><pPIS>1.65</pPIS><vPIS>1.65</vPIS></PISAliq></PIS>
                <COFINS><COFINSAliq><CST>01</CST><vBC>100.00</vBC><pCOFINS>7.60</pCOFINS><vCOFINS>7.60</vCOFINS></COFINSAliq></COFINS>
              </imposto>
            </det>
            <total>
              <ICMSTot>
                <vBC>100.00</vBC><vICMS>18.00</vICMS><vICMSDeson>0.00</vICMSDeson><vFCP>0.00</vFCP><vBCST>0.00</vBCST><vST>0.00</vST><vFCPST>0.00</vFCPST><vFCPSTRet>0.00</vFCPSTRet><vProd>100.00</vProd><vFrete>0.00</vFrete><vSeg>0.00</vSeg><vDesc>0.00</vDesc><vII>0.00</vII><vIPI>0.00</vIPI><vIPIDevol>0.00</vIPIDevol><vPIS>1.65</vPIS><vCOFINS>7.60</vCOFINS><vOutro>0.00</vOutro><vNF>100.00</vNF>
              </ICMSTot>
            </total>
            <transp><modFrete>9</modFrete></transp>
            <pag><detPag><tPag>90</tPag><vPag>0.00</vPag></detPag></pag>
          </infNFe>
          <Signature xmlns="http://www.w3.org/2000/09/xmldsig#"><SignedInfo /></Signature>
        </NFe>
        """;

    private static string CriarProtNFe() => """
        <protNFe versao="4.00" xmlns="http://www.portalfiscal.inf.br/nfe">
          <infProt>
            <tpAmb>2</tpAmb>
            <verAplic>SP_NFE_PL009_V4</verAplic>
            <chNFe>35260612345678000195550010000000011000000010</chNFe>
            <dhRecbto>2026-06-25T10:00:03-03:00</dhRecbto>
            <nProt>135000000000000</nProt>
            <digVal>AAAAAAAAAAAAAAAAAAAAAAAAAAA=</digVal>
            <cStat>100</cStat>
            <xMotivo>Autorizado o uso da NF-e</xMotivo>
          </infProt>
        </protNFe>
        """;

    private static string CriarRetornoSefazRejeitado() => """
        <?xml version="1.0" encoding="utf-8"?>
        <soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope">
          <soap:Body>
            <nfeResultMsg xmlns="http://www.portalfiscal.inf.br/nfe/wsdl/NFeAutorizacao4">
              <retEnviNFe versao="4.00" xmlns="http://www.portalfiscal.inf.br/nfe">
                <tpAmb>2</tpAmb>
                <cStat>104</cStat>
                <xMotivo>Lote processado</xMotivo>
                <protNFe versao="4.00">
                  <infProt>
                    <tpAmb>2</tpAmb>
                    <chNFe>35260612345678000195550010000000011000000010</chNFe>
                    <nProt>135000000000000</nProt>
                    <cStat>267</cStat>
                    <xMotivo>Rejeição: Chave de Acesso referenciada inexistente [nRef: 1]</xMotivo>
                  </infProt>
                </protNFe>
              </retEnviNFe>
            </nfeResultMsg>
          </soap:Body>
        </soap:Envelope>
        """;

    private static string CriarXmlEventoAssinadoMinimo() => """
        <evento versao="1.00" xmlns="http://www.portalfiscal.inf.br/nfe">
          <infEvento Id="ID1101113526061234567800019555001000000001100000001001">
            <cOrgao>35</cOrgao>
            <tpAmb>2</tpAmb>
            <CNPJ>12345678000195</CNPJ>
            <chNFe>35260612345678000195550010000000011000000010</chNFe>
            <dhEvento>2026-06-25T10:00:00-03:00</dhEvento>
            <tpEvento>110111</tpEvento>
            <nSeqEvento>1</nSeqEvento>
            <verEvento>1.00</verEvento>
            <detEvento versao="1.00">
              <descEvento>Cancelamento</descEvento>
              <nProt>135000000000000</nProt>
              <xJust>Erro operacional identificado apos autorizacao</xJust>
            </detEvento>
          </infEvento>
          <Signature xmlns="http://www.w3.org/2000/09/xmldsig#"><SignedInfo /></Signature>
        </evento>
        """;

    private static string CriarRetornoEventoAutorizado() => """
        <?xml version="1.0" encoding="utf-8"?>
        <soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope">
          <soap:Body>
            <nfeResultMsg xmlns="http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4">
              <retEnvEvento versao="1.00" xmlns="http://www.portalfiscal.inf.br/nfe">
                <idLote>1</idLote>
                <tpAmb>2</tpAmb>
                <verAplic>SP_EVENTOS</verAplic>
                <cOrgao>35</cOrgao>
                <cStat>128</cStat>
                <xMotivo>Lote de Evento Processado</xMotivo>
                <retEvento versao="1.00">
                  <infEvento>
                    <tpAmb>2</tpAmb>
                    <verAplic>SP_EVENTOS</verAplic>
                    <cOrgao>35</cOrgao>
                    <cStat>135</cStat>
                    <xMotivo>Evento registrado e vinculado a NF-e</xMotivo>
                    <chNFe>35260612345678000195550010000000011000000010</chNFe>
                    <tpEvento>110111</tpEvento>
                    <xEvento>Cancelamento</xEvento>
                    <nSeqEvento>1</nSeqEvento>
                    <nProt>135000000000001</nProt>
                  </infEvento>
                </retEvento>
              </retEnvEvento>
            </nfeResultMsg>
          </soap:Body>
        </soap:Envelope>
        """;

    private static string CriarRetornoInutilizacaoAutorizada() => """
        <?xml version="1.0" encoding="utf-8"?>
        <soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope">
          <soap:Body>
            <nfeResultMsg xmlns="http://www.portalfiscal.inf.br/nfe/wsdl/NFeInutilizacao4">
              <retInutNFe versao="4.00" xmlns="http://www.portalfiscal.inf.br/nfe">
                <infInut>
                  <tpAmb>2</tpAmb>
                  <verAplic>SP_INUTILIZACAO</verAplic>
                  <cStat>102</cStat>
                  <xMotivo>Inutilizacao de numero homologado</xMotivo>
                  <cUF>35</cUF>
                  <ano>26</ano>
                  <CNPJ>12345678000195</CNPJ>
                  <mod>55</mod>
                  <serie>1</serie>
                  <nNFIni>10</nNFIni>
                  <nNFFin>12</nNFFin>
                  <dhRecbto>2026-06-25T10:00:03-03:00</dhRecbto>
                  <nProt>135000000000002</nProt>
                </infInut>
              </retInutNFe>
            </nfeResultMsg>
          </soap:Body>
        </soap:Envelope>
        """;
}
