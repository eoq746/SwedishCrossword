# ---------------------------------------------------------------------------
# Multi-stage Dockerfile for SwedishCrossword.Api
# ---------------------------------------------------------------------------
# Build:   docker build -t svensktkorsord-api .
# Run:     docker run -p 8080:8080 -v crossword-data:/data svensktkorsord-api
# ---------------------------------------------------------------------------

# --- Build stage -----------------------------------------------------------
ARG DOTNET_VERSION=10.0
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Copy project files first for better layer caching on restore
COPY SwedishCrossword.sln ./
COPY SwedishCrossword.Core/SwedishCrossword.Core.csproj SwedishCrossword.Core/
COPY SwedishCrossword.Api/SwedishCrossword.Api.csproj SwedishCrossword.Api/
COPY SwedishCrossword/SwedishCrossword.csproj SwedishCrossword/
COPY ClueHandler/ClueHandler.csproj ClueHandler/
COPY SwedishCrossword.Tests/SwedishCrossword.Tests.csproj SwedishCrossword.Tests/
RUN dotnet restore SwedishCrossword.Api/SwedishCrossword.Api.csproj

# Copy everything and publish
COPY . .
RUN dotnet publish SwedishCrossword.Api/SwedishCrossword.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Copy dictionary data files into the publish output
RUN cp -r SwedishCrossword/Data /app/publish/Data

# --- Runtime stage ---------------------------------------------------------
ARG DOTNET_VERSION=10.0
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
