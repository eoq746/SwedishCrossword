# Svenskt Korsord (Swedish Crossword)

A Swedish crossword puzzle generator

**Play the daily puzzle:** [svensktkorsord.se](https://svensktkorsord.se)

## Features

- **Smart Crossword Generation**: Adaptive algorithm that creates well-connected puzzles with high fill percentages (65–75%)
- **Vinkelord (Bent Words)**: Supports L-shaped words that change direction at a bend cell, adding variety to the grid layout
- **Swedish Dictionary**: 100,000+ Swedish words with clues from Lexin, synonym pairs, the Kelly frequency list, DSSO, and a custom word file
- **Multiple Puzzle Sizes**: Small (9×9), Mobile (10×10), Easy (11×11), Medium (15×15), and Hard (17×17) presets — the web player offers 10×10, 15×15, and 17×17 via a unified landing-page card grid (size picker + archive link, extensible by adding entries to `PuzzleWarmupService.PuzzleSizes`)
- **Daily Puzzles**: Pre-generates today's puzzle plus 7 days ahead at startup, with hourly refresh; generates all configured sizes (10×10, 15×15, 17×17) per day
- **API-First Architecture**: Output caching, Brotli + Gzip response compression, per-IP rate limiting, security headers, CORS, and OpenAPI documentation
- **Interactive Web Player**: Browser-based crossword player with:
  - Keyboard navigation (arrow keys, space to toggle direction, Tab/Shift+Tab between clues)
  - Progress tracking and timer with `localStorage` persistence
  - Hint system: reveal a single letter or an entire word via server-side validation (tracked and penalized on leaderboard)
  - Social sharing: Wordle-style emoji grid with solve time, shareable via Web Share API or clipboard
  - Dark mode with system theme detection (`prefers-color-scheme`), manual toggle, and `localStorage` persistence — consistent across all pages via a CSS custom-property design-token system (85+ design tokens)
  - Styled modal system (confirm/message pattern) for user interactions
  - Shared leaderboard with medal podium for top 3, filtered by the current puzzle size
  - Historical leaderboard showing top scores from the past 30 days, filtered by puzzle size (entries are grouped by puzzle when multiple puzzles occur on the same date)
  - Player statistics per size: total solved, current/best streak, best time, average time — with automatic migration from legacy flat format
  - Puzzle archive calendar with size-filter toggle buttons
  - Server-computed difficulty rating displayed per puzzle
  - Mobile-responsive design (portrait and landscape modes) with collapsible panels and custom on-screen keyboard
  - 503 handling: friendly "puzzle generating" page when puzzles aren't ready yet
- **PWA**: Service worker with shell + puzzle caching for offline play (`sw.js`)
- **Accessibility**: ARIA labels and roles on all interactive elements, skip link, screen reader announcements via `aria-live` region, keyboard shortcuts dialog (`?` to toggle)
- **Security**: HMAC-signed submission tokens, Content Security Policy (CSP) headers, X-Content-Type-Options, X-Frame-Options, Referrer-Policy, Permissions-Policy, HSTS in production, Kestrel server header suppressed
- **Authentication**: Optional sign-in via Google or Microsoft OAuth (cookie-based, 30-day sliding expiration). Opaque user identity derived from SHA256 hash of provider + subject claim — raw provider IDs are never stored
- **User Profiles**: Signed-in users get a profile page with customisable alias, server-synced solve statistics, and friends management
- **Friends System**: Send/accept/decline friend requests by alias, view a private friends leaderboard on the puzzle page. All friend data uses opaque IDs — no raw user identifiers are exposed to the frontend
- **Server-Side Answer Validation**: Answers stripped from client JSON; `POST /api/puzzle/check` and `POST /api/puzzle/hint` endpoints validate against server-stored answers with token authentication
- **Anti-cheat System**: HMAC-signed submission tokens (issued when a puzzle is fetched, required when submitting a score) with minimum solve-time enforcement, plus client-side DevTools detection and solution-view tracking via localStorage
- **Bonus Words**: Detects valid accidental words formed during generation and includes them as extra clues
- **Analytics Dashboard**: Server-side analytics endpoints providing aggregate summary stats, daily breakdowns, and top player rankings from historical leaderboard data
- **Clue Handler Tool**: Standalone CLI for managing the dictionary — view statistics, add words, edit clues, auto-populate clues from Wiktionary, and generate compound/pattern-based clues

## Project Structure

```
SwedishCrosswords/
|-- SwedishCrossword.Core/          # Shared domain library (Models + Services)
|   |-- Models/                     # Domain models
|   |   |-- Word.cs                 # Word with clue, metadata, and segments
|   |   |-- WordSegment.cs          # Segment of a bent word path
|   |   |-- CrosswordGrid.cs        # Grid state, placement, and validation
|   |   |-- GridCell.cs             # Individual cell data
|   |   +-- AccidentalWord.cs       # Bonus word detection
|   +-- Services/                   # Core services
|       |-- CrosswordGenerator.cs   # Main generation orchestrator
|       |-- SwedishDictionary.cs    # Word lookup and filtering (loads all 5 sources)
|       |-- GridValidator.cs        # Puzzle validation
|       |-- PrintService.cs         # Output formatting (JSON, text)
|       |-- ClueGenerator.cs        # Clue generation
|       |-- LexinWordImporter.cs    # Lexin XML parser
|       |-- SynonymPairImporter.cs  # Synonym XML parser
|       |-- KellyWordImporter.cs    # Kelly word list importer
|       |-- DssoWordImporter.cs     # DSSO source file parser
|       |-- DataDirectory.cs        # Data file path resolution
|       |-- SafeJsonEncoder.cs      # JSON serialization with Swedish character support
|       +-- Generation/             # Generation sub-components
|           |-- WordPlacer.cs       # Anchor selection and adaptive placement
|           |-- WordAnalyzer.cs     # Connectivity scoring with disk cache
|           |-- GapFiller.cs        # Gap and bridge filling strategies
|           |-- VinkelordPlacer.cs  # Bent word opportunity detection
|           |-- GenerationHelpers.cs # Shared utility functions
|           +-- GenerationModels.cs # Internal generation models
|-- SwedishCrossword.Api/           # ASP.NET Core Minimal API
|   |-- Program.cs                  # API entry point (service registration, middleware, endpoint mapping)
|   |-- Endpoints/                  # Endpoint route definitions
|   |   |-- PuzzleEndpoints.cs      # Puzzle CRUD, check, hint, and dates endpoints
|   |   |-- LeaderboardEndpoints.cs # Score submission, leaderboard, and history endpoints
|   |   |-- AuthEndpoints.cs        # OAuth login, logout, profile, alias management
|   |   |-- FriendsEndpoints.cs     # Friend requests, friend list, friends leaderboard
|   |   |-- StatsEndpoints.cs       # Dictionary statistics endpoint
|   |   +-- AnalyticsEndpoints.cs   # Analytics summary, daily breakdown, and top players
|   |-- PuzzleWarmupService.cs      # Background service: pre-generates puzzles 7 days ahead
|   |-- SubmissionTokenService.cs   # HMAC-signed token generation/validation, answer stripping, server-side answer reading
|   |-- LeaderboardStore.cs         # SQLite-based leaderboard storage with history and analytics
|   |-- Models.cs                   # Request/response records (including analytics models)
|   |-- wwwroot/                    # Frontend (served by the API)
|   |   |-- index.html              # Landing page with SEO structured data
|   |   |-- puzzle.html             # Interactive crossword player page
|   |   |-- calendar.html           # Puzzle archive calendar
|   |   |-- profile.html            # User profile (alias, stats, friends management)
|   |   |-- site.js                 # Game logic (~3,300 lines, 15 §-numbered sections)
|   |   |-- site.min.css            # Responsive styles with 85+ CSS design tokens
|   |   |-- sw.js                   # Service worker (shell + puzzle caching)
|   |   |-- om-oss.html             # About page
|   |   |-- kontakt.html            # Contact page
|   |   |-- integritetspolicy.html  # Privacy policy
|   |   |-- robots.txt              # Search engine crawl rules
|   |   |-- sitemap.xml             # Sitemap for search engines
|   |   +-- site.webmanifest        # PWA manifest
|   |-- appsettings.json            # Configuration
|   +-- Properties/launchSettings.json
|-- SwedishCrossword/               # CLI generator
|   |-- Data/                       # Dictionary data files
|   |   |-- lexin-words.json        # Lexin dictionary (imported)
|   |   |-- synonym-words.json      # Synonym pairs (imported)
|   |   |-- kelly-words.json        # Kelly word list (imported)
|   |   |-- kelly-clues.json        # Curated clue overrides for Kelly words
|   |   |-- dsso-words.json         # DSSO dictionary (imported from source file)
|   |   +-- custom-words.json       # Custom/hand-curated words loaded at runtime
|   +-- Program.cs                  # CLI entry point
|-- ClueHandler/                    # Dictionary management tool
|   |-- Program.cs                  # CLI: statistics, add words, edit clues, Wiktionary lookup
|   |-- WiktionaryClueService.cs    # Auto-populate clues from Swedish Wiktionary dump
|   |-- CompoundClueGenerator.cs    # Generate clues for compound words via DSSO metadata
|   +-- PatternClueGenerator.cs     # Generate clues using morphological patterns
|-- SwedishCrossword.Tests/         # TUnit test project (core domain tests)
|-- SwedishCrossword.Api.Tests/     # API integration + unit tests
|   |-- ApiIntegrationTests.cs      # 58 integration tests (endpoints, leaderboard, analytics, check/hint)
|   |-- SubmissionTokenServiceTests.cs # 16 unit tests (token generation, validation, answer stripping)
|   +-- LeaderboardStoreTests.cs    # 59 unit tests (SQLite storage, dedup, pruning, analytics, friends)
|-- Dockerfile                      # Container build for the API
|-- scripts/                        # Operational scripts
|   +-- reset-data.ps1              # Clear stale leaderboard history and legacy puzzle files from Azure Files share
|-- infra/                          # Azure infrastructure (Bicep)
|   +-- main.bicep                  # Container Apps, ACR, Storage, Log Analytics
+-- .github/workflows/              # GitHub Actions
    +-- deploy-azure.yml            # Build, push & deploy to Azure Container Apps
```

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Running the API

```bash
# Clone the repository
git clone https://github.com/eoq746/SwedishCrossword.git
cd SwedishCrossword

# Run the API (serves frontend + REST endpoints)
dotnet run --project SwedishCrossword.Api
```

The API starts at `https://localhost:50579` and serves the crossword player at the root URL. Puzzles are pre-generated by a background service and cached to disk.

**API Endpoints:**

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/puzzle/today` | Get today's puzzle (`?size=10x10\|15x15\|17x17`, default `17x17`) |
| GET | `/api/puzzle/{yyyy-MM-dd}` | Get puzzle for a specific date (`?size=10x10\|15x15\|17x17`, default `17x17`) |
| POST | `/api/puzzle/check` | Validate answers against server-stored solutions (token-authenticated) |
| POST | `/api/puzzle/hint` | Reveal letter(s) from server-stored solutions (token-authenticated) |
| GET | `/api/puzzle/dates` | List available puzzle dates with per-date size arrays (`[{ date, sizes[] }]`) |
| GET | `/api/stats` | Dictionary statistics, available difficulties, and `availableSizes` |
| POST | `/api/scores` | Submit a score (token-validated, rate-limited) |
| GET | `/api/leaderboard` | Current leaderboard |
| POST | `/api/leaderboard/history` | Submit a historical score |
| GET | `/api/leaderboard/history?days=30` | Get historical scores (up to 90 days) |
| GET | `/api/analytics/summary` | Aggregate analytics: total completions, unique players, avg/best time, hint rates |
| GET | `/api/analytics/daily?days=30` | Per-day analytics breakdown (completions, players, times) for the last N days |
| GET | `/api/analytics/players?limit=10` | Top players ranked by games played with avg/best time |
| GET | `/api/auth/login/{provider}` | Initiate OAuth login (google or microsoft) |
| GET | `/api/auth/me` | Current user profile (name, alias, avatar) |
| POST | `/api/auth/logout` | Sign out and clear auth cookie |
| GET | `/api/auth/my-stats` | Server-synced solve statistics for signed-in user |
| GET | `/api/auth/alias` | Get current alias |
| PUT | `/api/auth/alias` | Set or update alias (2–20 chars, unique) |
| GET | `/api/friends` | List accepted friends |
| GET | `/api/friends/requests` | Pending friend requests (incoming + outgoing) |
| POST | `/api/friends/request` | Send friend request by alias |
| POST | `/api/friends/accept/{id}` | Accept a friend request |
| POST | `/api/friends/decline/{id}` | Decline a friend request |
| DELETE | `/api/friends/{id}` | Remove a friend |
| GET | `/api/friends/leaderboard?date=` | Friends leaderboard for a given date |
| GET | `/api/health` | Health check |

### Running with Docker

```bash
docker build -t svensktkorsord-api .
docker run -p 8080:8080 -v crossword-data:/data svensktkorsord-api
```

Puzzles and leaderboard data are persisted in the `/data` volume.

### Deploying to Azure Container Apps

The `infra/main.bicep` template provisions everything needed:

| Resource | Purpose |
|----------|--------|
| Azure Container Registry | Hosts the Docker image (Basic SKU) |
| Container Apps Environment | Serverless container host |
| Storage Account + Azure Files | Persistent `/data` volume for puzzles & leaderboard |
| Log Analytics Workspace | Container logs and monitoring |
| User-Assigned Managed Identity | Secure ACR pull (no admin credentials) |

**One-time setup (run manually with your own Azure CLI identity — creates the role assignment that CI/CD skips):**

```bash
# 1. Create a resource group
az group create --name rg-svensktkorsord --location swedencentral

# 2. Generate an HMAC secret for submission token signing
SECRET=$(openssl rand -base64 64)

# 3. Deploy infrastructure (uses a placeholder image — no real image needed yet)
#    This also creates the ACR pull role assignment (requires Owner / User Access Administrator)
az deployment group create \
  --resource-group rg-svensktkorsord \
  --template-file infra/main.bicep \
  --parameters submissionTokenSecret="$SECRET"

# 4. Build and push the first image
ACR_NAME=$(az deployment group show -g rg-svensktkorsord -n main --query 'properties.outputs.acrName.value' -o tsv)
az acr build --registry $ACR_NAME --image svensktkorsord:latest .

# 5. Re-deploy infrastructure with the real image tag to wire up ACR registry
az deployment group create \
  --resource-group rg-svensktkorsord \
  --template-file infra/main.bicep \
  --parameters imageTag=latest submissionTokenSecret="$SECRET"
```

**CI/CD:** The `deploy-azure.yml` workflow automatically builds and deploys on every push to `master`. It passes `createRoleAssignment=false` to skip the role assignment (already created during one-time setup). It requires the following repository secrets:
- `AZURE_CLIENT_ID` — App registration client ID (OIDC)
- `AZURE_TENANT_ID` — Entra ID tenant
- `AZURE_SUBSCRIPTION_ID` — Target subscription
- `SUBMISSION_TOKEN_SECRET` — HMAC secret for anti-cheat submission token signing (generate with `openssl rand -base64 64`)

### Running the CLI Generator

```bash
dotnet run --project SwedishCrossword
```

### Menu Options

1. **Generate Easy Crossword (11×11)** - Quick puzzles
2. **Generate Medium Crossword (15×15)** - Standard puzzles
3. **Generate Hard Crossword (17×17)** - Challenging puzzles with vinkelord
4. **Show Dictionary Statistics** - Word count, categories, lengths
5. **Import from Lexin** - Download and parse Lexin dictionary
6. **Import Synonym Pairs** - Parse Folkets synonymlexikon
7. **Import Kelly Words** - Parse the Kelly frequency word list
8. **Generate for Web** - Creates puzzle.json and starts local server
9. **Import from DSSO** - Parse Den Stora Svenska Ordlistan source file

## Dictionary Tools

### ClueHandler

```bash
dotnet run --project ClueHandler
```

Interactive menu:

1. **Visa ordlistestatistik** — Word count and category breakdown
2. **Lägg till nya ord** — Add individual words with custom clues
3. **Redigera ledtrådar** — Edit clues for existing dictionary entries
4. **Hämta ledtrådar från Wiktionary** — Auto-populate missing clues from the Swedish Wiktionary dump

Headless commands:

```bash
dotnet run --project ClueHandler -- --wiktionary   # Batch Wiktionary clue lookup
dotnet run --project ClueHandler -- --compounds     # Generate compound-word clues from DSSO metadata
dotnet run --project ClueHandler -- --patterns      # Generate clues via morphological pattern matching
```

## Running Tests

```bash
# Run unit tests
dotnet test SwedishCrossword.Tests

# Run API integration tests
dotnet test SwedishCrossword.Api.Tests
```

The test suite uses **[TUnit](https://github.com/thomhurst/TUnit)** (v0.4.1) and includes 372 tests:
- Grid cell and word model tests
- Grid placement and connectivity tests
- Swedish character handling tests (Å, Ä, Ö)
- Dictionary loading and validation tests
- Puzzle validation and bonus word tests
- Vinkelord (bent word) placement tests
- Vinkelord intertwining edge-case tests (overlapping bends, accidental words)
- Print service output tests
- SafeJsonEncoder serialization tests
- CrosswordGenerationOptions preset and computed-property tests
- GenerationHelpers utility method tests
- API integration tests (endpoint validation, leaderboard, analytics, score submission, puzzle check/hint, health checks)
- SubmissionTokenService unit tests (token generation, validation, access checks, answer stripping, expiry)
- LeaderboardStore unit tests (SQLite storage, deduplication, pruning, analytics aggregation, JSON migration, friend requests, friends leaderboard)

## Algorithm Highlights

### Generation Pipeline
The generator is split into specialized components orchestrated by `CrosswordGenerator`:

1. **WordAnalyzer** — Pre-computes connectivity scores for candidate words and caches results to disk for fast subsequent runs.
2. **WordPlacer** — Selects anchor words for the initial scaffold and runs an adaptive placement loop with batched word placement.
3. **GapFiller** — Scans rows and columns for patterns of existing letters with gaps, then finds dictionary words that match those patterns.
4. **VinkelordPlacer** — Detects L-shaped opportunities on the grid and matches them against dictionary words to place bent words (vinkelord).

### Word Selection
- Prioritizes words with common Swedish letters (A, E, R, S, T, N) and high vowel counts
- Balances across and down word placement using direction-aware scoring
- Uses intersection potential scoring for anchor words
- Connectivity scores are cached to disk so repeated runs skip the expensive analysis

### Vinkelord (Bent Words)
- Words that change direction at a bend cell, forming an L-shape on the grid
- The bend cell is shared between two segments and rendered with a directional arrow
- Configurable maximum bends per word and maximum vinkelord count per puzzle
- The web player correctly navigates and highlights bent words using a cell-to-clue lookup that considers local direction at each cell

### Validation
- Ensures all words are connected (no isolated words)
- Prevents duplicate word usage
- Validates accidental word formations against dictionary during placement
- Rollback-on-failure: each placement is tested against a targeted cell backup and reverted if it creates invalid words

### Performance Optimizations
- **Targeted backup/restore**: Only cells along the placed word's path are saved and restored on rollback, reducing per-attempt cost from O(W×H) to O(word length)
- **Suppressed renumbering**: Clue renumbering is deferred until after generation completes, eliminating an O(W×H) pass on every placement attempt
- **Cached grid statistics**: `GetStats()` results are cached and invalidated only when the grid changes
- **Cached isolation checks**: Word-endpoint positions are cached in HashSets for O(1) lookup instead of iterating all words per cell
- **O(1) dictionary clue lookup**: Accidental-word validation uses direct dictionary key lookup instead of materializing and scanning the full word list

### Quality Metrics
- Target fill percentage: 65%+
- Minimum word count based on grid size
- Proper word isolation (no unintended adjacencies)

## Dictionary Sources

### Lexin (Primary)
The main dictionary is sourced from [Lexin](https://spraakbanken.gu.se/resurser/lexin), a Swedish-foreign language lexicon maintained by ISOF (Institute for Language and Folklore).

### Folkets synonymlexikon (Synonyms)
Additional synonym pairs from [Folkets synonymlexikon](http://lexikon.nada.kth.se/synlex.html), providing word-to-synonym clues.

### Kelly Word List (Frequency)
Frequency-ranked vocabulary from the [Kelly project](https://spraakbanken.gu.se/resurser/kelly), categorized by CEFR level (A1–C2). Clues are generated from a curated clue dictionary (`kelly-clues.json`) with POS-based fallback patterns.

### DSSO (Den Stora Svenska Ordlistan)
A comprehensive Swedish word list from [DSSO](https://dsso.se/) (version 1.51). The source data file is parsed and exported to `dsso-words.json`. Clues are sourced from DSSO definitions, supplemented by Wiktionary lookups and compound/pattern-based generators. Licensed under [CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/).

### Custom Words
A hand-curated `custom-words.json` file for words not covered by the main sources. Loaded last so custom entries take precedence over imported ones.

**Combined Statistics:**
- ~100,000+ words across all five sources
- Categories: Substantiv, Verb, Adjektiv, Adverb, etc.
- Difficulty levels: Easy, Medium, Hard
- Full support for Swedish characters (Å, Ä, Ö)

## Web Architecture

- **Runtime**: ASP.NET Core Minimal API (`SwedishCrossword.Api`) serving both the frontend and REST endpoints
- **Puzzle Storage**: File-based, configurable via `Storage:PuzzlePath` (env: `Storage__PuzzlePath`)
- **Leaderboard**: SQLite-based store (`LeaderboardStore.cs`) using WAL mode, configurable via `Storage:LeaderboardPath`, with per-puzzle deduplication, 7-day pruning, historical archival, user aliases, friend requests, and automatic migration from legacy JSON files on startup
- **Deployment**: Docker container on Azure Container Apps (or any ASP.NET Core host)
- **Shared Library**: `SwedishCrossword.Core` contains all domain models and services, referenced by both the API and CLI
- **Daily Generation**: `PuzzleWarmupService` pre-generates today's puzzle plus 7 days ahead at startup and refreshes hourly; all configured sizes (10×10, 15×15, 17×17) are generated per day via an extensible `PuzzleSizes` array; word-analysis scores are cached to disk for fast subsequent runs
- **Submission Tokens**: `SubmissionTokenService` generates HMAC-signed tokens when puzzles are fetched and validates them on score submission, enforcing minimum solve time per cell and a 48-hour token lifetime. The signing secret is configured via `SubmissionToken:Secret` (env: `SubmissionToken__Secret`); if not set, an ephemeral key is generated at startup (logged as a warning)
- **Server-Side Answer Validation**: Puzzle JSON is stripped of answers before serving to clients; `POST /api/puzzle/check` validates submitted cell values and `POST /api/puzzle/hint` reveals requested letters, both authenticated via submission tokens
- **Output Caching**: Puzzle responses are cached (5 min for today, 1 hour for archive, 10 min for dates) to reduce disk reads
- **Response Compression**: Brotli + Gzip enabled for JSON and static assets
- **Rate Limiting**: Global per-IP limit (200 req/min) plus stricter limits on leaderboard writes, puzzle interactions, and friend operations (30 req/min each)
- **Authentication**: Cookie-based with Google and Microsoft OAuth providers (configured via `Authentication:Google:ClientId`/`ClientSecret` and `Authentication:Microsoft:ClientId`/`ClientSecret`). 30-day sliding expiration. User identity is a SHA256 hash of `provider:subject` — raw provider IDs are never stored. Providers are conditionally registered only when credentials are configured.
- **Friends**: Friend requests stored in `friend_requests` SQLite table with statuses (pending/accepted/declined). All API responses use opaque friendship IDs and server-computed direction — no raw user identifiers are exposed to clients.
- **Security Headers**: Content-Security-Policy, X-Content-Type-Options, X-Frame-Options, Referrer-Policy, Permissions-Policy, HSTS in production; Kestrel `Server` header suppressed; request body size capped at 100 KB
- **Forwarded Headers**: Configured for Azure App Service reverse proxy (`X-Forwarded-For`, `X-Forwarded-Proto`)
- **CORS**: Configurable via `Cors:AllowedOrigins` in appsettings
- **OpenAPI**: Available at `/openapi` in development mode
- **PWA**: Service worker (`sw.js`) with shell caching (cache-first with background update) and puzzle API caching (network-first with offline fallback)
- **Accessibility**: Skip link, ARIA labels/roles on grid, clue lists, dialogs, and buttons; `aria-live` region for screen reader announcements; keyboard shortcuts dialog
- **Endpoint Organization**: API routes are split into dedicated static classes under `Endpoints/` (`PuzzleEndpoints`, `LeaderboardEndpoints`, `AuthEndpoints`, `FriendsEndpoints`, `StatsEndpoints`, `AnalyticsEndpoints`), each registered as an extension method on `WebApplication`
- **Analytics**: `LeaderboardStore` exposes aggregate queries (summary, daily breakdown, top players) consumed by the analytics endpoints
- **Frontend Organization**: `site.js` (~3,300 lines) uses a table of contents with 15 `§`-numbered section headers for navigability
- **Solution-View Tracking**: Client-side via localStorage so the anti-cheat system can flag players who viewed the answer before submitting

## License

The dictionary data is licensed under [Creative Commons Attribution 2.5 Sweden](https://creativecommons.org/licenses/by/2.5/se/). DSSO words are licensed under [Creative Commons Attribution-ShareAlike 3.0](https://creativecommons.org/licenses/by-sa/3.0/).

## Contributing

Contributions are welcome! Please feel free to submit issues or pull requests.

### Areas for Improvement
- Themed puzzle generation
- Mobile app version (native)
- Mini leagues / friend groups
- Core Web Vitals tracking
- Frontend module splitting (ES modules with bundler)
- Client-side unit tests
- Static asset fingerprinting / cache-busting
- CDN / Azure Front Door for edge caching

## Acknowledgments

- [Lexin/ISOF](https://spraakbanken.gu.se/resurser/lexin) for the Swedish dictionary
- [Folkets synonymlexikon](http://lexikon.nada.kth.se/synlex.html) for synonym pairs
- [Kelly word list](https://spraakbanken.gu.se/resurser/kelly) for frequency-ranked vocabulary
- [DSSO (Den Stora Svenska Ordlistan)](https://dsso.se/) for comprehensive Swedish word coverage
- [Swedish Wiktionary](https://sv.wiktionary.org) for supplementary word definitions