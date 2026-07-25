# Debian أكثر استقراراً على Render Free من Alpine (تجنّب exit 139 مع SQLite/ICU)
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_NOLOGO=1 \
    NUGET_XMLDOC_MODE=skip \
    MSBUILDTERMINALLOGGER=off

COPY ShakabaArchive.Core/ShakabaArchive.Core.csproj ShakabaArchive.Core/
COPY ShakabaArchive.Web/ShakabaArchive.Web.csproj ShakabaArchive.Web/
RUN dotnet restore ShakabaArchive.Web/ShakabaArchive.Web.csproj --verbosity quiet

COPY ShakabaArchive.Core/ ShakabaArchive.Core/
COPY ShakabaArchive.Web/ ShakabaArchive.Web/

RUN dotnet publish ShakabaArchive.Web/ShakabaArchive.Web.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    -m:1 \
    /p:BuildInParallel=false \
    /p:UseSharedCompilation=false \
    /p:PublishReadyToRun=false \
    /p:DebugType=None \
    /p:DebugSymbols=false \
    --verbosity quiet

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_GCServer=0 \
    DOTNET_GCHeapCount=1 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

EXPOSE 8080
COPY --from=build /app/publish .

CMD ["sh", "-c", "export ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080}; exec dotnet ShakabaArchive.Web.dll"]
