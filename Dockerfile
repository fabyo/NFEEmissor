# Estágio de build usando o SDK do .NET 10
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copia os arquivos de solução e projetos para restaurar dependências
COPY NfeEmissor.slnx ./
COPY src/Nfe.Shared/Nfe.Shared.csproj src/Nfe.Shared/
COPY src/Nfe.Core/Nfe.Core.csproj src/Nfe.Core/
COPY src/Nfe.Api/Nfe.Api.csproj src/Nfe.Api/
COPY src/Nfe.Cli/Nfe.Cli.csproj src/Nfe.Cli/
COPY tests/Nfe.UnitTests/Nfe.UnitTests.csproj tests/Nfe.UnitTests/

# Restaura dependências (buscando do NuGet público de forma limpa)
RUN dotnet restore src/Nfe.Api/Nfe.Api.csproj

# Copia o restante do código do emissor
COPY src/ src/

# Compila a API em modo Release
RUN dotnet publish src/Nfe.Api/Nfe.Api.csproj -c Release -o /app/publish

# Baixa e converte a cadeia ICP-Brasil fora da imagem runtime.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS icp-certs

RUN set -eu; \
    apt-get update; \
    apt-get install -y --no-install-recommends ca-certificates unzip; \
    curl -fsSL "https://letsencrypt.org/certs/2024/e7.pem" \
        -o /tmp/lets-encrypt-e7.pem; \
    fingerprint=$(openssl x509 -in /tmp/lets-encrypt-e7.pem -noout -fingerprint -sha256); \
    if [ "$fingerprint" != "sha256 Fingerprint=54:71:54:20:22:4C:5B:65:BE:ED:01:8D:C3:94:0D:73:38:C5:77:E3:22:D5:48:8F:63:3D:8C:6A:8F:ED:61:B2" ]; then \
        echo "Fingerprint inesperado para o intermediario Let's Encrypt E7: $fingerprint" >&2; \
        exit 1; \
    fi; \
    curl --cacert /tmp/lets-encrypt-e7.pem -fsSL "https://acraiz.icpbrasil.gov.br/credenciadas/CertificadosAC-ICP-Brasil/ACcompactado.zip" \
        -o /tmp/icp.zip; \
    unzip -q /tmp/icp.zip -d /tmp/icp-certs; \
    mkdir -p /icp-ca; \
    find /tmp/icp-certs -type f \( -iname '*.cer' -o -iname '*.crt' \) -exec sh -c ' \
        set -eu; \
        out_dir="$1"; \
        shift; \
        for f do \
            name=$(basename "$f"); \
            name=${name%.*}; \
            out="$out_dir/$name.crt"; \
            if openssl x509 -in "$f" -out "$out" 2>/dev/null; then \
                :; \
            elif openssl x509 -inform DER -in "$f" -out "$out" 2>/dev/null; then \
                :; \
            else \
                echo "Falha ao converter certificado ICP-Brasil: $f" >&2; \
                exit 1; \
            fi; \
        done \
    ' sh /icp-ca {} +; \
    converted=$(find /icp-ca -type f -name '*.crt' | wc -l); \
    if [ "$converted" -eq 0 ]; then \
        echo "Nenhum certificado ICP-Brasil foi convertido." >&2; \
        exit 1; \
    fi; \
    echo "Certificados ICP-Brasil convertidos: $converted"; \
    rm -rf /tmp/icp.zip /tmp/icp-certs /tmp/lets-encrypt-e7.pem /var/lib/apt/lists/*

# Estágio final usando o ASP.NET Core Runtime (.NET 10)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# Instala a cadeia ICP-Brasil no trust store do Linux.
COPY --from=icp-certs /icp-ca/ /usr/local/share/ca-certificates/

RUN set -eu; \
    apt-get update; \
    apt-get install -y --no-install-recommends ca-certificates curl; \
    update-ca-certificates; \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

# Expondo a porta padrão da API do container
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Nfe.Api.dll"]
