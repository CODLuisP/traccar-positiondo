# ─── Build ────────────────────────────────────────────────────────────────────
# .NET 10 no publica imágenes bookworm/trixie (solo Ubuntu noble/resolute, Alpine, Azure Linux).
# Se usa noble (Ubuntu 24.04), que tiene apt funcional.
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

COPY TraccarAgent/TraccarAgent.csproj TraccarAgent/
# El .csproj fija RuntimeIdentifier=win-x64; se sobreescribe con linux-x64
RUN dotnet restore TraccarAgent/TraccarAgent.csproj -r linux-x64

COPY TraccarAgent/ TraccarAgent/
RUN dotnet publish TraccarAgent/TraccarAgent.csproj -c Release -r linux-x64 --self-contained false --no-restore -o /app/publish

# ─── Runtime ──────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime

ENV TZ=America/Lima \
    DEBIAN_FRONTEND=noninteractive \
    ASPNETCORE_HTTP_PORTS=5008 \
    DOTNET_RUNNING_IN_CONTAINER=true

RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata curl ca-certificates \
    && ln -snf /usr/share/zoneinfo/$TZ /etc/localtime && echo $TZ > /etc/timezone \
    && rm -rf /var/lib/apt/lists/*

RUN groupadd --gid 10001 traccar \
    && useradd --uid 10001 --gid 10001 --no-create-home --shell /usr/sbin/nologin traccar

WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /app/data && chown -R 10001:10001 /app/data

USER 10001:10001

EXPOSE 5008

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS http://127.0.0.1:5008/health || exit 1

ENTRYPOINT ["dotnet", "TraccarAgent.dll"]
