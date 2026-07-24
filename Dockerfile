# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ShakabaArchive.Core/ShakabaArchive.Core.csproj ShakabaArchive.Core/
COPY ShakabaArchive.Web/ShakabaArchive.Web.csproj ShakabaArchive.Web/
RUN dotnet restore ShakabaArchive.Web/ShakabaArchive.Web.csproj
COPY ShakabaArchive.Core/ ShakabaArchive.Core/
COPY ShakabaArchive.Web/ ShakabaArchive.Web/
RUN dotnet publish ShakabaArchive.Web/ShakabaArchive.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ShakabaArchive.Web.dll"]
