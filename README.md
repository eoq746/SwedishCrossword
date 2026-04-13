# Svenskt Korsord (Swedish Crossword)

A Swedish crossword puzzle generator

**Play the daily puzzle:** [svensktkorsord.se](https://svensktkorsord.se)

## Features

- **Smart Crossword Generation**: Adaptive algorithm that creates well-connected puzzles with high fill percentages (65–75%)
- **Vinkelord (Bent Words)**: Supports L-shaped words that change direction at a bend cell, adding variety to the grid layout
- **Swedish Dictionary**: 100,000+ Swedish words with clues from Lexin, synonym pairs, the Kelly frequency list, DSSO, and a custom word file
- **Multiple Puzzle Sizes**: Small (9×9), Mobile (10×10), Easy (11×11), Medium (15×15), and Hard (17×17) presets
- **Daily Puzzles**: Pre-generates today's puzzle plus 7 days ahead at startup, with hourly refresh; both standard and small/mobile variants
- **API-First Architecture**: Output caching, Brotli + Gzip response compression, per-IP rate limiting, security headers, CORS, and OpenAPI documentation
- **Interactive Web Player**: Browser-based crossword player with:
  - Keyboard navigation (arrow keys, space to toggle direction, Tab/Shift+Tab between clues)
  - Progress tracking and timer
  - Shared leaderboard with medal podium for top 3
  - Historical leaderboard showing top scores from the past 30 days (entries are grouped by puzzle when multiple puzzles occur on the same date)
  - Mobile-responsive design (portrait and landscape modes)
- **Anti-cheat System**: HMAC-signed submission tokens (issued when a puzzle is fetched, required when submitting a score) with minimum solve-time enforcement, plus client-side DevTools detection and solution-view tracking via localStorage
- **Bonus Words**: Detects valid accidental words formed during generation and includes them as extra clues
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
|   |-- Program.cs                  # API entry point (puzzle, score, leaderboard endpoints)
|   |-- PuzzleWarmupService.cs      # Background service: pre-generates puzzles 7 days ahead
|   |-- SubmissionTokenService.cs   # HMAC-signed token generation and validation for score submissions
|   |-- wwwroot/                    # Frontend (served by the API)
|   |   |-- index.html              # Main crossword player
|   |   |-- puzzle.html             # Individual puzzle page
|   |   |-- calendar.html           # Puzzle archive calendar
|   |   |-- site.js                 # Game logic, navigation, and leaderboard
|   |   |-- site.min.css            # Responsive styles
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
|-- SwedishCrossword.Tests/         # TUnit test project
|-- SwedishCrossword.Api.Tests/     # API integration tests
|-- Dockerfile                      # Container build for the API
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
| GET | `/api/puzzle/today` | Get today's puzzle (with `?size=small` for mobile variant) |
| GET | `/api/puzzle/{yyyy-MM-dd}` | Get puzzle for a specific date (with `?size=small` for mobile variant) |
| GET | `/api/puzzle/dates` | List available puzzle dates |
| GET | `/api/stats` | Dictionary statistics and available difficulties |
| POST | `/api/scores` | Submit a score (token-validated, rate-limited) |
| GET | `/api/leaderboard` | Current leaderboard |
| POST | `/api/leaderboard/history` | Submit a historical score |
| GET | `/api/leaderboard/history?days=30` | Get historical scores (up to 90 days) |
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

# 2. Deploy infrastructure (uses a placeholder image — no real image needed yet)
#    This also creates the ACR pull role assignment (requires Owner / User Access Administrator)
az deployment group create \
  --resource-group rg-svensktkorsord \
  --template-file infra/main.bicep

# 3. Build and push the first image
ACR_NAME=$(az deployment group show -g rg-svensktkorsord -n main --query 'properties.outputs.acrName.value' -o tsv)
az acr build --registry $ACR_NAME --image svensktkorsord:latest .

# 4. Re-deploy infrastructure with the real image tag to wire up ACR registry
az deployment group create \
  --resource-group rg-svensktkorsord \
  --template-file infra/main.bicep \
  --parameters imageTag=latest
```

**CI/CD:** The `deploy-azure.yml` workflow automatically builds and deploys on every push to `master`. It passes `createRoleAssignment=false` to skip the role assignment (already created during one-time setup). It requires three repository secrets:
- `AZURE_CLIENT_ID` — App registration client ID (OIDC)
- `AZURE_TENANT_ID` — Entra ID tenant
- `AZURE_SUBSCRIPTION_ID` — Target subscription

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

The test suite uses **[TUnit](https://github.com/thomhurst/TUnit)** (v0.4.1) and includes:
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
- API integration tests (endpoint validation, leaderboard, health checks)

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
- **Leaderboard**: Built-in file-based store, configurable via `Storage:LeaderboardPath`
- **Deployment**: Docker container or any ASP.NET Core host
- **Shared Library**: `SwedishCrossword.Core` contains all domain models and services, referenced by both the API and CLI
- **Daily Generation**: `PuzzleWarmupService` pre-generates today's puzzle plus 7 days ahead at startup and refreshes hourly; both standard and small/mobile variants are generated; word-analysis scores are cached to disk for fast subsequent runs
- **Submission Tokens**: `SubmissionTokenService` generates HMAC-signed tokens when puzzles are fetched and validates them on score submission, enforcing minimum solve time per cell and a 48-hour token lifetime
- **Output Caching**: Puzzle responses are cached (5 min for today, 1 hour for archive, 10 min for dates) to reduce disk reads
- **Response Compression**: Brotli + Gzip enabled for JSON and static assets
- **Rate Limiting**: Global per-IP limit (200 req/min) plus stricter limits on leaderboard writes (30 req/min)
- **Security Headers**: X-Content-Type-Options, X-Frame-Options, Referrer-Policy, Permissions-Policy, and HSTS in production
- **CORS**: Configurable via `Cors:AllowedOrigins` in appsettings
- **OpenAPI**: Available at `/openapi` in development mode
- **Solution-View Tracking**: Client-side via localStorage so the anti-cheat system can flag players who viewed the answer before submitting

## License

The dictionary data is licensed under [Creative Commons Attribution 2.5 Sweden](https://creativecommons.org/licenses/by/2.5/se/). DSSO words are licensed under [Creative Commons Attribution-ShareAlike 3.0](https://creativecommons.org/licenses/by-sa/3.0/).

## Contributing

Contributions are welcome! Please feel free to submit issues or pull requests.

### Areas for Improvement
- Archive of previous puzzles
- Difficulty-based word selection
- Themed puzzle generation
- User statistics and progress tracking
- Mobile app version

## Acknowledgments

- [Lexin/ISOF](https://spraakbanken.gu.se/resurser/lexin) for the Swedish dictionary
- [Folkets synonymlexikon](http://lexikon.nada.kth.se/synlex.html) for synonym pairs
- [Kelly word list](https://spraakbanken.gu.se/resurser/kelly) for frequency-ranked vocabulary
- [DSSO (Den Stora Svenska Ordlistan)](https://dsso.se/) for comprehensive Swedish word coverage
- [Swedish Wiktionary](https://sv.wiktionary.org) for supplementary word definitions