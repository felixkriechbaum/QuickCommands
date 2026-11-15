# syntax=docker/dockerfile:1
# Build context: the server/ folder.  docker build -t quickcommands-server server
# Cross-compiles on the build machine's architecture (no QEMU emulation of the .NET build for arm64).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
ARG VERSION=0.0.0
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY src/QuickCommands.Server/QuickCommands.Server.csproj src/QuickCommands.Server/
RUN dotnet restore src/QuickCommands.Server/QuickCommands.Server.csproj -a $TARGETARCH

COPY src/ src/
# The OpenAPI document is generated (and checked) by CI, not inside the image build.
RUN dotnet publish src/QuickCommands.Server/QuickCommands.Server.csproj -c Release -a $TARGETARCH --no-restore -o /app \
    -p:Version=$VERSION -p:OpenApiGenerateDocuments=false

# Chiseled: no shell, no package manager, runs as non-root user "app".
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
# No curl in the image – the app checks /api/health itself and exits with 0 or 1.
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 CMD ["dotnet", "QuickCommands.Server.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "QuickCommands.Server.dll"]
