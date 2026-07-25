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
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
COPY --from=build /app/publish .

# Render يمرّر PORT؛ الافتراضي 8080 محلياً
CMD ["sh", "-c", "dotnet ShakabaArchive.Web.dll --urls http://0.0.0.0:${PORT:-8080}"]
