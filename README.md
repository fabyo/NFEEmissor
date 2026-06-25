# NFEEmissor

> Status: projeto em evolução. A emissão em homologação já foi testada, mas uso em produção exige validação fiscal, jurídica e operacional no cenário da sua empresa.

## Leia antes de usar

### Sobre a responsabilidade pelo uso

Este projeto nasceu de uma necessidade real e foi construído com cuidado — mas NF-e é um ecossistema complexo. Regras tributárias mudam, cada UF tem suas peculiaridades, cada regime fiscal tem suas exigências, e cada empresa tem um cenário diferente dos demais.

Antes de usar em produção: teste muito. Valide no ambiente de homologação da SEFAZ. Revise os XMLs gerados com alguém que entenda do processo fiscal da sua empresa.

A legislação, as regras de CSOSN/CST, os parâmetros de ICMS, PIS e COFINS variam por UF, regime tributário e atividade econômica — e essa variação é sua responsabilidade conhecer e configurar corretamente.

Ao usar este projeto, você assume total responsabilidade pelos documentos emitidos. O autor disponibiliza o código de boa-fé, mas não presta suporte fiscal nem se responsabiliza por erros, rejeições ou penalidades.

## O que este projeto faz

Este projeto emite NF-e modelo 55 usando .NET, certificado digital A1 e webservices da SEFAZ.

Ele possui:

- `Nfe.Api`: API HTTP para emissão assíncrona, consulta de status e consulta da chave na SEFAZ.
- `Nfe.Core`: geração de XML, assinatura digital, envio para SEFAZ e validações.
- `Nfe.Cli`: utilitário local para gerar e assinar XML sem enviar para a SEFAZ.
- `Nfe.Shared`: contratos de entrada usados pela API e pelo CLI.

Dependências principais:

- `NFEConsulta`: usada para consulta de status do serviço e consulta de NF-e pela chave de acesso.
- `NFEDanfe`: usada para geração de DANFE em PDF a partir de XML autorizado (`procNFe.xml`).
- `NFeSchemaDownloader`: usada pela API para sincronizar schemas XSD oficiais quando necessário.

## Pacotes NuGet

O empacotamento é separado por responsabilidade:

- `NFEEmissor.Core`: biblioteca principal para geração, assinatura e autorização.
- `NFEEmissor.Shared`: contratos/DTOs compartilhados.
- `NFEEmissor.Cli`: ferramenta `dotnet tool` com o comando `nfe-emissor` para gerar XML assinado localmente.

`Nfe.Api` não é empacotado como NuGet; ele é uma aplicação HTTP para rodar via Docker ou publicação própria.

Para empacotar localmente:

```bash
dotnet pack NfeEmissor.Packages.slnx -o ./artifacts/packages
```

Para instalar o CLI como tool a partir de um pacote local:

```bash
dotnet tool install --global NFEEmissor.Cli \
  --add-source ./artifacts/packages \
  --version 0.2.0
```

Depois de instalado:

```bash
nfe-emissor --help
```

Quando os pacotes estiverem publicados no NuGet:

```bash
dotnet add package NFEEmissor.Core --version 0.2.0
dotnet add package NFEEmissor.Shared --version 0.2.0
dotnet tool install --global NFEEmissor.Cli --version 0.2.0
```

Licença: MIT.

## Requisitos

- Docker e Docker Compose.
- Certificado digital A1 em PEM ou PFX.
- Dados fiscais reais e coerentes com o certificado.

Os certificados devem ficar em `certs/`. Essa pasta é ignorada pelo Git.

Exemplo esperado:

```text
certs/cert.pem
certs/key.pem
certs/cert.pfx
```

## Subindo a API

```bash
docker compose up -d --build
```

A API fica disponível em:

```text
http://localhost:5000
```

Serviços auxiliares:

- Redis: `localhost:6379`
- Seq: `http://localhost:8085`

## Modelo stateless

A API foi desenhada para não ser o repositório definitivo dos documentos fiscais.

- Não há banco de dados obrigatório.
- Redis é usado apenas para fila, idempotência curta, backoff temporário da SEFAZ e status com TTL.
- O status retorna o `xmlResult` (`procNFe.xml`) e, quando solicitado, `danfePdfBase64`.
- A aplicação cliente deve persistir o XML autorizado e o DANFE em seu próprio storage, banco, disco, S3/MinIO ou sistema fiscal.
- Para integrar persistência sem mudar o fluxo da API, implemente `INfeStorage`. A implementação padrão é `NoopNfeStorage`, que não grava nada.

Por padrão, o resultado temporário expira em 12 horas. Depois disso, a API pode retornar `404` para o `correlationId`.

## Emitindo uma NF-e

Use o arquivo `nota-teste.json` como base. Em homologação, mantenha:

```json
{
  "ambienteEmissao": "2",
  "destinatario": {
    "nomeRazaoSocial": "NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL"
  }
}
```

### Emitir usando certificado PEM

```bash
CERT=$(base64 -w0 certs/cert.pem)
KEY=$(base64 -w0 certs/key.pem)

curl -sS -X POST "http://localhost:5000/api/v1/nfe/emitir?gerarDanfe=false" \
  -H "Content-Type: application/json" \
  -H "X-Cert-Pem-Base64: $CERT" \
  -H "X-Key-Pem-Base64: $KEY" \
  --data-binary @nota-teste.json
```

Resposta esperada:

```json
{
  "correlationId": "3f8a5d63ad894b998b810e509fdf9c4c",
  "status": "Pendente",
  "message": "A nota fiscal foi colocada na fila de processamento."
}
```

### Emitir usando certificado PFX

```bash
CERT=$(base64 -w0 certs/cert.pfx)

curl -sS -X POST "http://localhost:5000/api/v1/nfe/emitir?gerarDanfe=false" \
  -H "Content-Type: application/json" \
  -H "X-Certificado-Base64: $CERT" \
  -H "X-Certificado-Senha: sua-senha" \
  --data-binary @nota-teste.json
```

## Consultando o status local da emissão

Depois de emitir, consulte pelo `correlationId` retornado:

```bash
curl -sS "http://localhost:5000/api/v1/nfe/status/3f8a5d63ad894b998b810e509fdf9c4c"
```

Resposta autorizada:

```json
{
  "correlationId": "3f8a5d63ad894b998b810e509fdf9c4c",
  "status": "Autorizada",
  "chaveAcesso": "35260612345678000195550010000000011000000010",
  "protocolo": "135000000000000",
  "xmlResult": "<?xml version=\"1.0\" encoding=\"utf-8\"?><nfeProc ...",
  "danfePdfBase64": "JVBERi0xLjQK...",
  "expiraEm": "2026-06-25T18:00:00+00:00",
  "ttlSegundos": 43200,
  "storage": {
    "persistido": false,
    "xmlProcNfeUri": null,
    "danfePdfUri": null
  }
}
```

Salve `xmlResult` como `procNFe.xml`. Se `danfePdfBase64` vier preenchido, decodifique o Base64 e salve como PDF.

## Consultando a chave direto na SEFAZ

Use o endpoint de consulta quando você já tiver uma chave NF-e de 44 dígitos.

### Consulta em homologação com PEM

```bash
CERT=$(base64 -w0 certs/cert.pem)
KEY=$(base64 -w0 certs/key.pem)

curl -sS "http://localhost:5000/api/v1/nfe/consulta?chave=35260612345678000195550010000000011000000010&uf=SP&ambiente=2" \
  -H "X-Cert-Pem-Base64: $CERT" \
  -H "X-Key-Pem-Base64: $KEY"
```

Resposta:

```json
{
  "status": "100",
  "motivo": "Autorizado o uso da NF-e",
  "protocolo": "135000000000000",
  "xmlRetorno": null
}
```

### Consulta em produção

Troque `ambiente=2` por `ambiente=1`:

```bash
curl -sS "http://localhost:5000/api/v1/nfe/consulta?chave=SUA_CHAVE&uf=SP&ambiente=1" \
  -H "X-Cert-Pem-Base64: $CERT" \
  -H "X-Key-Pem-Base64: $KEY"
```

## Consultando status do serviço SEFAZ

```bash
CERT=$(base64 -w0 certs/cert.pfx)

curl -sS "http://localhost:5000/api/v1/nfe/status-servico?uf=SP&ambiente=2" \
  -H "X-Certificado-Base64: $CERT" \
  -H "X-Certificado-Senha: sua-senha"
```

## Lendo informações do certificado

```bash
CERT=$(base64 -w0 certs/cert.pfx)

curl -sS -X POST "http://localhost:5000/api/v1/certificado/info" \
  -H "X-Certificado-Base64: $CERT" \
  -H "X-Certificado-Senha: sua-senha"
```

## Gerando XML assinado sem enviar para a SEFAZ

Use o CLI quando quiser apenas gerar e assinar o XML localmente.

Com PEM:

```bash
docker run --rm \
  -v "$PWD:/src" \
  -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet run --project src/Nfe.Cli/Nfe.Cli.csproj -- \
    emitir \
    --json nota-teste.json \
    --cert certs/cert.pem \
    --key certs/key.pem \
    --output-dir out
```

Com PFX:

```bash
docker run --rm \
  -v "$PWD:/src" \
  -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet run --project src/Nfe.Cli/Nfe.Cli.csproj -- \
    emitir \
    --json nota-teste.json \
    --cert certs/cert.pfx \
    --senha sua-senha \
    --output-dir out
```

O XML assinado será salvo em `out/`.

## Gerando DANFE em PDF

Use um XML autorizado/processado (`*-procNFe.xml`). XML apenas assinado, sem protocolo de autorização, não é suficiente para um DANFE fiscalmente válido.

Na API, informe `gerarDanfe=true` ao emitir:

```bash
curl -sS -X POST "http://localhost:5000/api/v1/nfe/emitir?gerarDanfe=true" \
  -H "Content-Type: application/json" \
  -H "X-Cert-Pem-Base64: $CERT" \
  -H "X-Key-Pem-Base64: $KEY" \
  --data-binary @nota-teste.json
```

O status retornará `danfePdfBase64`. Decodifique esse valor e salve como PDF na aplicação cliente.

A geração de PDF usa QuestPDF por meio da dependência `NFEDanfe`; o projeto configura a licença como `LicenseType.Community` antes de gerar o PDF:

```csharp
QuestPDF.Settings.License = LicenseType.Community;
```

O pacote `NFEEmissor.Cli` não inclui geração de DANFE para evitar um pacote de ferramenta muito grande. Use a API ou a dependência `NFEDanfe` diretamente para DANFE.

## CNPJ alfanumérico e Reforma Tributária

O projeto aceita CNPJ com letras, preservando os 14 caracteres alfanuméricos no XML e na chave de acesso. Pontuação é removida automaticamente:

```json
{
  "cnpj": "12.ABC.345/0001-88"
}
```

Também há suporte inicial ao grupo `IBSCBS` nos impostos do item. O projeto escreve os campos informados e agrega os totais em `IBSCBSTot`, mas não calcula automaticamente enquadramento, CST, `cClassTrib` ou alíquotas. Esses valores devem vir do sistema fiscal/tributário do emissor.

Exemplo:

```json
{
  "impostos": {
    "ibsCbs": {
      "cst": "410",
      "codigoClassificacaoTributaria": "410999",
      "baseCalculo": 100.0,
      "ibsUf": {
        "aliquota": 0.1,
        "valor": 0.1
      },
      "ibsMunicipio": {
        "aliquota": 0.0,
        "valor": 0.0
      },
      "cbs": {
        "aliquota": 0.9,
        "valor": 0.9
      }
    }
  }
}
```

## Exemplo mínimo de payload

```json
{
  "ambienteEmissao": "2",
  "serie": "1",
  "numeroNfe": 8,
  "naturezaOperacao": "VENDA DE MERCADORIA",
  "tipoOperacao": "1",
  "formatoImpressaoDanfe": "1",
  "tipoEmissao": "1",
  "finalidadeEmissao": "1",
  "consumidorFinal": "1",
  "indicadorPresencaComprador": "9",
  "emitente": {
    "cnpj": "12345678000195",
    "razaoSocial": "EMPRESA EMITENTE TESTE LTDA",
    "nomeFantasia": "EMPRESA EMITENTE TESTE LTDA",
    "inscricaoEstadual": "110042490114",
    "cnaeFiscal": "2500000",
    "codigoRegimeTributario": "3",
    "endereco": {
      "logradouro": "RUA TESTE",
      "numero": "100",
      "bairro": "CENTRO",
      "codigoMunicipio": "3547809",
      "nomeMunicipio": "SAO PAULO",
      "uf": "SP",
      "cep": "01001000",
      "codigoPais": "1058",
      "nomePais": "BRASIL"
    }
  },
  "destinatario": {
    "cnpj": "99999999000191",
    "nomeRazaoSocial": "NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL",
    "indicadorIe": "9",
    "endereco": {
      "logradouro": "AVENIDA CLIENTE",
      "numero": "200",
      "bairro": "JARDINS",
      "codigoMunicipio": "3550308",
      "nomeMunicipio": "SAO PAULO",
      "uf": "SP",
      "cep": "02002000",
      "codigoPais": "1058",
      "nomePais": "Brasil"
    }
  },
  "produtos": [
    {
      "codigoProduto": "EB.007",
      "descricao": "CACAMBA No2 METALICA ONDULADA 1040X959X660 VW.00001 TARA 70KG",
      "ncmSh": "73090090",
      "cfop": "5102",
      "unidadeComercial": "PC",
      "quantidadeComercial": 3.0,
      "valorUnitarioComercial": 500.0,
      "valorBruto": 1500.0,
      "unidadeTributavel": "PC",
      "quantidadeTributavel": 3.0,
      "valorUnitarioTributavel": 500.0,
      "indicadorComposicaoTotal": "1",
      "impostos": {
        "icms": {
          "cst": "00",
          "origem": "0",
          "baseCalculo": 1500.0,
          "aliquota": 18.0,
          "valor": 270.0
        },
        "pis": {
          "cst": "01",
          "baseCalculo": 1500.0,
          "aliquota": 1.65,
          "valor": 24.75
        },
        "cofins": {
          "cst": "01",
          "baseCalculo": 1500.0,
          "aliquota": 7.6,
          "valor": 114.0
        }
      }
    }
  ],
  "transporte": {
    "modalidadeFrete": "9"
  },
  "pagamentos": [
    {
      "meioPagamento": "15",
      "valor": 1500.0
    }
  ]
}
```

## Observações importantes

- `ambienteEmissao`: `1` para produção, `2` para homologação.
- `indicadorComposicaoTotal`: `1` compõe o total da NF-e, `0` não compõe.
- Em homologação, a razão social do destinatário deve ser `NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL`.
- CST com benefício fiscal pode exigir `cBenef`, conforme regra da UF.
- O projeto aplica backoff temporário quando a SEFAZ retorna `656 - Consumo Indevido`.
- `certs/`, `out/`, `tmp-nfe-out/`, `schemas/` e `*-procNFe.xml` são ignorados pelo Git.

## Testes

```bash
docker run --rm \
  -v "$PWD:/src" \
  -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet test tests/Nfe.UnitTests/Nfe.UnitTests.csproj
```
