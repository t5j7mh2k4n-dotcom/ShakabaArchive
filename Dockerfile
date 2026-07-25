FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ShakabaArchive.Core/ShakabaArchive.Core.csproj ShakabaArchive.Core/
COPY ShakabaArchive.Web/ShakabaArchive.Web.csproj ShakabaArchive.Web/
RUN dotnet restore ShakabaArchive.Web/ShakabaArchive.Web.csproj

COPY ShakabaArchive.Core/ ShakabaArchive.Core/
COPY ShakabaArchive.Web/ ShakabaArchive.Web/
RUN dotnet publish ShakabaArchive.Web/ShakabaArchive.Web.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:PublishReadyToRun=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# تقليل استهلاك الذاكرة على Render Free
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV DOTNET_EnableDiagnostics=0
ENV DOTNET_GCServer=0
ENV DOTNET_GCHeapCount=1
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

EXPOSE 8080
COPY --from=build /app/publish .

CMD ["sh", "-c", "export ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080}; exec dotnet ShakabaArchive.Web.dll"]
