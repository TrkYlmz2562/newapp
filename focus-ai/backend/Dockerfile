# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore first, on manifests only, so the (slow) restore layer is cached until
# a dependency actually changes.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/FocusAI.Domain/*.csproj src/FocusAI.Domain/
COPY src/FocusAI.Application/*.csproj src/FocusAI.Application/
COPY src/FocusAI.Infrastructure/*.csproj src/FocusAI.Infrastructure/
COPY src/FocusAI.Api/*.csproj src/FocusAI.Api/
RUN dotnet restore src/FocusAI.Api/FocusAI.Api.csproj

COPY . .
RUN dotnet publish src/FocusAI.Api/FocusAI.Api.csproj \
    -c Release \
    -o /app \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# libicu and tzdata are mandatory, not optional. Without ICU, Unicode
# normalisation silently stops decomposing Turkish characters (breaking
# de-duplication) and only the UTC time zone resolves (breaking every reader's
# local 08:00 digest). See Directory.Build.props.
RUN apt-get update \
    && apt-get install --no-install-recommends -y libicu72 tzdata curl \
    && rm -rf /var/lib/apt/lists/*

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true

# Run unprivileged; the image needs no write access to its own filesystem.
RUN useradd --create-home --shell /usr/sbin/nologin focusai
COPY --from=build --chown=focusai:focusai /app ./
USER focusai

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "FocusAI.Api.dll"]
