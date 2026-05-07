# ---------------------------------------------------------------------------
# Multi-stage Dockerfile for SwedishCrossword.Api
# ---------------------------------------------------------------------------
# Build:   docker build -t svensktkorsord-api .
# Run:     docker run -p 8080:8080 -v crossword-data:/data svensktkorsord-api
# ---------------------------------------------------------------------------

ARG DOTNET_VERSION=10.0
ARG NODE_MAJOR=24

# --- Build stage -----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl gnupg \
    && mkdir -p /etc/apt/keyrings \
    && curl -fsSL https://deb.nodesource.com/gpgkey/nodesource-repo.gpg.key | gpg --dearmor -o /etc/apt/keyrings/nodesource.gpg \
    && echo "deb [signed-by=/etc/apt/keyrings/nodesource.gpg] https://deb.nodesource.com/node_${NODE_MAJOR}.x nodistro main" > /etc/apt/sources.list.d/nodesource.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends nodejs \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# Copy project files first for better layer caching on restore
COPY SwedishCrossword.sln ./
COPY SwedishCrossword.Core/SwedishCrossword.Core.csproj SwedishCrossword.Core/
COPY SwedishCrossword.Api/SwedishCrossword.Api.csproj SwedishCrossword.Api/
COPY SwedishCrossword/SwedishCrossword.csproj SwedishCrossword/
COPY ClueHandler/ClueHandler.csproj ClueHandler/
COPY SwedishCrossword.Tests/SwedishCrossword.Tests.csproj SwedishCrossword.Tests/
COPY frontend/package.json frontend/package-lock.json frontend/
RUN dotnet restore SwedishCrossword.Api/SwedishCrossword.Api.csproj
RUN npm ci --prefix frontend

# Copy everything and publish
COPY . .
RUN dotnet publish SwedishCrossword.Api/SwedishCrossword.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Copy dictionary data files into the publish output
RUN cp -r SwedishCrossword/Data /app/publish/Data

# --- Runtime stage ---------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Persistent storage for puzzles and leaderboard (mount a volume here)
VOLUME ["/data"]

ENV ASPNETCORE_URLS=http://+:8080
ENV Storage__PuzzlePath=/data/puzzles
ENV Storage__LeaderboardPath=/data/leaderboard
ENV SWEDISH_CROSSWORD_CACHE_PATH=/data/cache

# Run as non-root. The official aspnet image ships a pre-created `app`
# user (uid 1654). Ensure mounted /data is writable by this user.
RUN mkdir -p /data && chown -R app:app /data /app
USER app

EXPOSE 8080

ENTRYPOINT ["dotnet", "SwedishCrossword.Api.dll"]
