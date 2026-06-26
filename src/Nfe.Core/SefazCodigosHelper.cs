namespace Nfe.Core;

/// <summary>
/// Tabelas de referência SEFAZ: códigos IBGE de UF e URLs de webservice por ambiente.
/// </summary>
public static class SefazCodigosHelper
{
    // ─── Código IBGE por UF ───────────────────────────────────────────────
    private static readonly Dictionary<string, int> CodigoIbgePorUf = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AC"] = 12, ["AL"] = 27, ["AP"] = 16, ["AM"] = 13,
        ["BA"] = 29, ["CE"] = 23, ["DF"] = 53, ["ES"] = 32,
        ["GO"] = 52, ["MA"] = 21, ["MT"] = 51, ["MS"] = 50,
        ["MG"] = 31, ["PA"] = 15, ["PB"] = 25, ["PR"] = 41,
        ["PE"] = 26, ["PI"] = 22, ["RJ"] = 33, ["RN"] = 24,
        ["RS"] = 43, ["RO"] = 11, ["RR"] = 14, ["SC"] = 42,
        ["SP"] = 35, ["SE"] = 28, ["TO"] = 17,
    };

    public static int ObterCodigoUf(string uf)
    {
        if (!CodigoIbgePorUf.TryGetValue(uf, out var codigo))
            throw new ArgumentException($"UF inválida: '{uf}'", nameof(uf));
        return codigo;
    }

    // ─── Código IBGE por nome de município (simplificado — use tabela completa) ──
    public static string ObterCodigoMunicipio(string nomeMunicipio, string uf)
    {
        // Em produção: consultar tabela IBGE completa (CSV/banco)
        // Aqui retorna o código passado no próprio DTO (CodigoMunicipio)
        throw new NotImplementedException(
            "Integre com a tabela IBGE de municípios. " +
            "Recomenda-se usar o campo CodigoMunicipio direto do DTO.");
    }

    // ─── URLs SEFAZ por UF e ambiente ─────────────────────────────────────
    // Referência: Manual de Orientação do Contribuinte v7.0 — Anexo I
    // UFs com autorizador próprio: AM, BA, GO, MG, MS, MT, PE, PR, RS, SP
    // Demais: SVRS (homolog) / SVC-AN (produção) via SEFAZ-RS/SEFAZ-AN
    private static readonly Dictionary<string, SefazEndpoints> EndpointsPorUf = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SP"] = new(
            AutorizacaoProd:    "https://nfe.fazenda.sp.gov.br/ws/nfeautorizacao4.asmx",
            AutorizacaoHom:     "https://homologacao.nfe.fazenda.sp.gov.br/ws/nfeautorizacao4.asmx",
            ConsultaProd:       "https://nfe.fazenda.sp.gov.br/ws/nfeconsultaprotocolo4.asmx",
            ConsultaHom:        "https://homologacao.nfe.fazenda.sp.gov.br/ws/nfeconsultaprotocolo4.asmx",
            InutilizacaoProd:   "https://nfe.fazenda.sp.gov.br/ws/nfeinutilizacao4.asmx",
            InutilizacaoHom:    "https://homologacao.nfe.fazenda.sp.gov.br/ws/nfeinutilizacao4.asmx",
            RetornoAutorizacaoProd: "https://nfe.fazenda.sp.gov.br/ws/nferetautorizacao4.asmx",
            RetornoAutorizacaoHom:  "https://homologacao.nfe.fazenda.sp.gov.br/ws/nferetautorizacao4.asmx",
            RecepcaoEventoProd: "https://nfe.fazenda.sp.gov.br/ws/nferecepcaoevento4.asmx",
            RecepcaoEventoHom:  "https://homologacao.nfe.fazenda.sp.gov.br/ws/nferecepcaoevento4.asmx"
        ),
        ["MG"] = new(
            AutorizacaoProd:    "https://nfe.fazenda.mg.gov.br/nfe2/services/NFeAutorizacao4",
            AutorizacaoHom:     "https://hnfe.fazenda.mg.gov.br/nfe2/services/NFeAutorizacao4",
            ConsultaProd:       "https://nfe.fazenda.mg.gov.br/nfe2/services/NFeConsultaProtocolo4",
            ConsultaHom:        "https://hnfe.fazenda.mg.gov.br/nfe2/services/NFeConsultaProtocolo4",
            InutilizacaoProd:   "https://nfe.fazenda.mg.gov.br/nfe2/services/NFeInutilizacao4",
            InutilizacaoHom:    "https://hnfe.fazenda.mg.gov.br/nfe2/services/NFeInutilizacao4",
            RetornoAutorizacaoProd: "https://nfe.fazenda.mg.gov.br/nfe2/services/NFeRetAutorizacao4",
            RetornoAutorizacaoHom:  "https://hnfe.fazenda.mg.gov.br/nfe2/services/NFeRetAutorizacao4",
            RecepcaoEventoProd: "https://nfe.fazenda.mg.gov.br/nfe2/services/NFeRecepcaoEvento4",
            RecepcaoEventoHom:  "https://hnfe.fazenda.mg.gov.br/nfe2/services/NFeRecepcaoEvento4"
        ),
        ["RS"] = new(
            AutorizacaoProd:    "https://nfe.sefaz.rs.gov.br/ws/NfeAutorizacao/NFeAutorizacao4.asmx",
            AutorizacaoHom:     "https://nfe-homologacao.sefazrs.rs.gov.br/ws/NfeAutorizacao/NFeAutorizacao4.asmx",
            ConsultaProd:       "https://nfe.sefaz.rs.gov.br/ws/NfeConsulta/NfeConsulta4.asmx",
            ConsultaHom:        "https://nfe-homologacao.sefazrs.rs.gov.br/ws/NfeConsulta/NfeConsulta4.asmx",
            InutilizacaoProd:   "https://nfe.sefaz.rs.gov.br/ws/NfeInutilizacao/NfeInutilizacao4.asmx",
            InutilizacaoHom:    "https://nfe-homologacao.sefazrs.rs.gov.br/ws/NfeInutilizacao/NfeInutilizacao4.asmx",
            RetornoAutorizacaoProd: "https://nfe.sefaz.rs.gov.br/ws/NfeRetAutorizacao/NfeRetAutorizacao4.asmx",
            RetornoAutorizacaoHom:  "https://nfe-homologacao.sefazrs.rs.gov.br/ws/NfeRetAutorizacao/NfeRetAutorizacao4.asmx",
            RecepcaoEventoProd: "https://nfe.sefaz.rs.gov.br/ws/recepcaoevento/recepcaoevento4.asmx",
            RecepcaoEventoHom:  "https://nfe-homologacao.sefazrs.rs.gov.br/ws/recepcaoevento/recepcaoevento4.asmx"
        ),
        ["PR"] = new(
            AutorizacaoProd:    "https://nfe.sefa.pr.gov.br/nfe/NFeAutorizacao4",
            AutorizacaoHom:     "https://homologacao.nfe.sefa.pr.gov.br/nfe/NFeAutorizacao4",
            ConsultaProd:       "https://nfe.sefa.pr.gov.br/nfe/NFeConsultaProtocolo4",
            ConsultaHom:        "https://homologacao.nfe.sefa.pr.gov.br/nfe/NFeConsultaProtocolo4",
            InutilizacaoProd:   "https://nfe.sefa.pr.gov.br/nfe/NFeInutilizacao4",
            InutilizacaoHom:    "https://homologacao.nfe.sefa.pr.gov.br/nfe/NFeInutilizacao4",
            RetornoAutorizacaoProd: "https://nfe.sefa.pr.gov.br/nfe/NFeRetAutorizacao4",
            RetornoAutorizacaoHom:  "https://homologacao.nfe.sefa.pr.gov.br/nfe/NFeRetAutorizacao4",
            RecepcaoEventoProd: "https://nfe.sefa.pr.gov.br/nfe/NFeRecepcaoEvento4",
            RecepcaoEventoHom:  "https://homologacao.nfe.sefa.pr.gov.br/nfe/NFeRecepcaoEvento4"
        ),
        // UFs que usam SVRS (Sefaz-RS como autorizador) em homologação:
        // AC, AL, AP, CE, DF, ES, PB, PI, RJ, RN, RO, RR, SC, SE, TO
        // Em produção usam SVC-AN
    };

    // ─── SVRS (Sefaz Virtual Rio Grande do Sul) — ambiente de homologação ──
    private static readonly SefazEndpoints EndpointsSvrs = new(
        AutorizacaoProd:    "https://nfe.svrs.rs.gov.br/ws/NfeAutorizacao/NFeAutorizacao4.asmx",
        AutorizacaoHom:     "https://nfe-homologacao.svrs.rs.gov.br/ws/NfeAutorizacao/NFeAutorizacao4.asmx",
        ConsultaProd:       "https://nfe.svrs.rs.gov.br/ws/NfeConsulta/NfeConsulta4.asmx",
        ConsultaHom:        "https://nfe-homologacao.svrs.rs.gov.br/ws/NfeConsulta/NfeConsulta4.asmx",
        InutilizacaoProd:   "https://nfe.svrs.rs.gov.br/ws/NfeInutilizacao/NfeInutilizacao4.asmx",
        InutilizacaoHom:    "https://nfe-homologacao.svrs.rs.gov.br/ws/NfeInutilizacao/NfeInutilizacao4.asmx",
        RetornoAutorizacaoProd: "https://nfe.svrs.rs.gov.br/ws/NfeRetAutorizacao/NfeRetAutorizacao4.asmx",
        RetornoAutorizacaoHom:  "https://nfe-homologacao.svrs.rs.gov.br/ws/NfeRetAutorizacao/NfeRetAutorizacao4.asmx",
        RecepcaoEventoProd: "https://nfe.svrs.rs.gov.br/ws/recepcaoevento/recepcaoevento4.asmx",
        RecepcaoEventoHom:  "https://nfe-homologacao.svrs.rs.gov.br/ws/recepcaoevento/recepcaoevento4.asmx"
    );

    public static SefazEndpoints ObterEndpoints(string uf)
        => EndpointsPorUf.TryGetValue(uf, out var ep) ? ep : EndpointsSvrs;

    public static string ObterUrlAutorizacao(string uf, string ambiente)
    {
        var ep = ObterEndpoints(uf);
        return ambiente == "1" ? ep.AutorizacaoProd : ep.AutorizacaoHom;
    }

    public static string ObterUrlRetornoAutorizacao(string uf, string ambiente)
    {
        var ep = ObterEndpoints(uf);
        return ambiente == "1" ? ep.RetornoAutorizacaoProd : ep.RetornoAutorizacaoHom;
    }

    public static string ObterUrlConsulta(string uf, string ambiente)
    {
        var ep = ObterEndpoints(uf);
        return ambiente == "1" ? ep.ConsultaProd : ep.ConsultaHom;
    }

    public static string ObterUrlInutilizacao(string uf, string ambiente)
    {
        var ep = ObterEndpoints(uf);
        return ambiente == "1" ? ep.InutilizacaoProd : ep.InutilizacaoHom;
    }

    public static string ObterUrlRecepcaoEvento(string uf, string ambiente)
    {
        var ep = ObterEndpoints(uf);
        return ambiente == "1" ? ep.RecepcaoEventoProd : ep.RecepcaoEventoHom;
    }
}

public sealed record SefazEndpoints(
    string AutorizacaoProd,
    string AutorizacaoHom,
    string ConsultaProd,
    string ConsultaHom,
    string InutilizacaoProd,
    string InutilizacaoHom,
    string RetornoAutorizacaoProd,
    string RetornoAutorizacaoHom,
    string RecepcaoEventoProd,
    string RecepcaoEventoHom
);
