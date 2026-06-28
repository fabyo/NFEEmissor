# Changelog

## 0.3.3 - 2026-06-28

- Fixadas por SHA as GitHub Actions usadas nos workflows de CI, publicação e Scorecard.
- Fixadas por digest as imagens .NET SDK e ASP.NET Runtime usadas no Dockerfile.
- Adicionada análise SAST com CodeQL em pushes e pull requests da branch `master`.
- Reforçada a proteção da branch padrão contra alterações não revisadas e reescrita de histórico.
- Corrigido o fechamento do leitor de schema XSD após a compilação.

## 0.3.2 - 2026-06-26

- O endpoint `GET /api/v1/nfe/status-servico` passou a aceitar PEM além de PFX, fechando a validação em homologação com o mesmo fluxo dos demais endpoints.
- Atualizada a documentação de uso e os exemplos de instalação para a nova versão `0.3.2`.

## 0.3.1 - 2026-06-26

- Adicionado suporte a eventos fiscais de NF-e: cancelamento (`110111`) e Carta de Correção Eletrônica (`110110`).
- Adicionado suporte a inutilização de numeração de NF-e modelo 55.
- Adicionados endpoints HTTP `POST /api/v1/nfe/cancelar`, `POST /api/v1/nfe/cce` e `POST /api/v1/nfe/inutilizar`.
- Adicionado suporte no CLI para gerar e assinar XMLs de cancelamento, CC-e e inutilização sem enviar para a SEFAZ.
- O endpoint `GET /api/v1/nfe/status-servico` agora aceita PEM além de PFX, alinhando a validação de homologação ao restante da API.
- Generalizada a assinatura XML por elemento com atributo `Id`, mantendo compatibilidade com a assinatura de `infNFe` e adicionando suporte a `infEvento` e `infInut`.
- Adicionados endpoints SEFAZ de recepção de eventos e acesso ao endpoint de inutilização por UF/ambiente.
- Adicionados testes offline para montagem de XML de cancelamento, CC-e, inutilização e parsing de retornos simulados da SEFAZ.
- Adicionado endpoint `GET /health` com verificação de Redis.
- Adicionados healthchecks no Dockerfile e no Docker Compose.
- Adicionado `global.json` fixando o SDK .NET `10.0.100` com roll-forward para latest feature.
- Adicionada proteção AES-256-GCM para certificados e senhas armazenados temporariamente na fila Redis, com chave configurável por `Nfe__QueueProtectionKey`.
- Adicionada validação de GTIN-8, GTIN-12, GTIN-13 e GTIN-14 para `cEAN` e `cEANTrib`.
- Tornado o carregamento de schemas XSD configurável por `Nfe__SchemasPath`, `Nfe__TiposBasicosSchema` e `Nfe__NfeSchema`, permitindo apontar para pacotes PL mais recentes como PL_010C/CNPJ Alfa.
- Ativada validação XSD antes de assinar/enviar NF-e na API, configurável por `Nfe__ValidateXsdBeforeSend`.
- Adicionado endpoint `GET /api/v1/nfe/schemas` para diagnosticar schemas carregados.
- Adicionada opção `--validar-xsd` no CLI para validar XML localmente antes da assinatura.
- Reforçadas validações RTC/IBSCBS para percentuais fora de faixa e normalização de `CST`/`cClassTrib`.
- Atualizada a versão dos pacotes para `0.3.1`.

## 0.2.1 - 2026-06-25

- Atualizada a versão dos pacotes para `0.2.1`.

## 0.2.0 - 2026-06-25

- Separado o empacotamento NuGet por responsabilidade: biblioteca principal (`NFEEmissor`) e ferramenta CLI (`NFEEmissor.Cli`), com os contratos compartilhados fundidos no pacote principal e a API fora do pacote NuGet.
- Adicionados metadados de pacote NuGet, incluindo título, descrição, tags, README e ícone `logo-200.png`.
- Adicionada solução `NfeEmissor.Packages.slnx` para empacotar apenas os projetos publicáveis.
- Formalizado o comportamento stateless da API: Redis usado apenas para fila/status temporário com TTL, documentos fiscais retornados no status e persistência definitiva delegada à aplicação cliente.
- Adicionada interface opcional `INfeStorage` com implementação padrão `NoopNfeStorage`, permitindo persistência externa sem obrigar banco ou storage no projeto base.
- Adicionado suporte a CNPJ alfanumérico em validação, geração de XML e chave de acesso.
- Adicionado suporte inicial aos campos da Reforma Tributária no grupo `IBSCBS`, incluindo CST, `cClassTrib`, bases/valores de IBS UF, IBS municipal, CBS e totais `IBSCBSTot`.
- Adicionados testes de integração offline para montar `nfeProc` com protocolo simulado e gerar DANFE sem chamar a SEFAZ.
- Adicionados testes de integração HTTP para a API cobrindo emissão sem certificado, consulta de status, consulta da chave e certificado, sem bater na SEFAZ.
- Alinhados `.gitignore` e `.dockerignore` para excluir certificados, XMLs fiscais, DANFEs, pacotes `.nupkg` e artefatos temporários.
- Ajustada a formatação decimal de valores unitários (`vUnCom`, `vUnTrib`) para remover zeros desnecessários sem perder precisão.
- Adicionada licença MIT.
- Adicionado workflow `ci.yml` para restore, testes, build da API e pack em pushes/PRs.
- Reduzido o pacote `NFEEmissor.Cli` removendo geração de DANFE do tool e mantendo `NFEDanfe`, `NFEConsulta` e `NFeSchemaDownloader` fora do pacote Core; DANFE, consulta SEFAZ e sincronização de schemas permanecem disponíveis na API.
- Ajustada a API para resolver Redis apenas após validação da requisição de emissão.
