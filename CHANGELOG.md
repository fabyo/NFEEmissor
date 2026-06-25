# Changelog

## 0.2.0 - 2026-06-25

- Separado o empacotamento NuGet por responsabilidade: biblioteca principal (`NFEEmissor`), `NFEEmissor.Shared`, ferramenta CLI (`NFEEmissor.Cli`) e API fora do pacote NuGet.
- Adicionados metadados de pacote NuGet, incluindo título, descrição, tags, README e ícone `logo-200.png`.
- Adicionada solução `NfeEmissor.Packages.slnx` para empacotar apenas os projetos publicáveis.
- Formalizado o comportamento stateless da API: Redis usado apenas para fila/status temporário com TTL, documentos fiscais retornados no status e persistência definitiva delegada à aplicação cliente.
- Adicionada interface opcional `INfeStorage` com implementação padrão `NoopNfeStorage`, permitindo persistência externa sem obrigar banco ou storage no projeto base.
- Adicionado suporte a CNPJ alfanumérico em validação, geração de XML e chave de acesso.
- Adicionado suporte inicial aos campos da Reforma Tributária no grupo `IBSCBS`, incluindo CST, `cClassTrib`, bases/valores de IBS UF, IBS municipal, CBS e totais `IBSCBSTot`.
- Adicionados testes de integração offline para montar `nfeProc` com protocolo simulado e gerar DANFE sem chamar a SEFAZ.
- Alinhados `.gitignore` e `.dockerignore` para excluir certificados, XMLs fiscais, DANFEs, pacotes `.nupkg` e artefatos temporários.
- Ajustada a formatação decimal de valores unitários (`vUnCom`, `vUnTrib`) para remover zeros desnecessários sem perder precisão.
- Adicionada licença MIT.
- Adicionado workflow `ci.yml` para restore, testes, build da API e pack em pushes/PRs.
- Atualizada versão dos pacotes para `0.2.0`.
- Reduzido o pacote `NFEEmissor.Cli` removendo geração de DANFE do tool e mantendo `NFEDanfe`, `NFEConsulta` e `NFeSchemaDownloader` fora do pacote Core; DANFE, consulta SEFAZ e sincronização de schemas permanecem disponíveis na API.
