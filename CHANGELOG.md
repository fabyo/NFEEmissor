# Changelog

## 0.1.0 - 2026-06-25

- Separado o empacotamento NuGet por responsabilidade: bibliotecas (`NFEEmissor.Core`, `NFEEmissor.Shared`), ferramenta CLI (`NFEEmissor.Cli`) e API fora do pacote NuGet.
- Adicionados metadados de pacote NuGet, incluindo título, descrição, tags, README e ícone `logo-200.png`.
- Adicionada solução `NfeEmissor.Packages.slnx` para empacotar apenas os projetos publicáveis.
- Formalizado o comportamento stateless da API: Redis usado apenas para fila/status temporário com TTL, documentos fiscais retornados no status e persistência definitiva delegada à aplicação cliente.
- Adicionada interface opcional `INfeStorage` com implementação padrão `NoopNfeStorage`, permitindo persistência externa sem obrigar banco ou storage no projeto base.
- Adicionado suporte a CNPJ alfanumérico em validação, geração de XML e chave de acesso.
- Adicionado suporte inicial aos campos da Reforma Tributária no grupo `IBSCBS`, incluindo CST, `cClassTrib`, bases/valores de IBS UF, IBS municipal, CBS e totais `IBSCBSTot`.
- Adicionados testes de integração offline para montar `nfeProc` com protocolo simulado e gerar DANFE sem chamar a SEFAZ.
- Alinhado `.dockerignore` ao `.gitignore` para excluir certificados, XMLs fiscais, DANFEs, pacotes `.nupkg` e artefatos temporários do contexto Docker.
- Ajustada a formatação decimal de valores unitários (`vUnCom`, `vUnTrib`) para remover zeros desnecessários sem perder precisão.
