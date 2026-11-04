# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY MuslimMatrimony.Web/MuslimMatrimony.csproj MuslimMatrimony.Web/
RUN dotnet restore MuslimMatrimony.Web/MuslimMatrimony.csproj
COPY MuslimMatrimony.Web/ MuslimMatrimony.Web/
RUN dotnet publish MuslimMatrimony.Web/MuslimMatrimony.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Storage__PhotoDirectory=/data/photos \
    DataProtection__KeysPath=/data/keys
WORKDIR /app
COPY --from=build /app ./
RUN mkdir -p /data/photos /data/keys && chown -R app:app /data
# The official .NET images ship a non-root "app" user.
USER app
EXPOSE 8080
VOLUME ["/data"]
# Probe GET /health from your orchestrator (it checks the database connection).
ENTRYPOINT ["dotnet", "MuslimMatrimony.dll"]
