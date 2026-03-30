# Svenskt Korsord (Swedish Crossword)

[![Daily Crossword Generation](https://github.com/eoq746/SwedishCrossword/actions/workflows/daily-crossword.yml/badge.svg)](https://github.com/eoq746/SwedishCrossword/actions/workflows/daily-crossword.yml)

A Swedish crossword puzzle generator and web player. Generates high-quality crossword puzzles using a Swedish dictionary based on [Lexin (ISOF)](https://spraakbanken.gu.se/resurser/lexin), [Folkets synonymlexikon](http://lexikon.nada.kth.se/synlex.html), the [Kelly word list](https://spraakbanken.gu.se/resurser/kelly), and [DSSO (Den Stora Svenska Ordlistan)](https://dsso.se/).

**Play the daily puzzle:** [svensktkorsord.se](https://svensktkorsord.se)

## Features

- **Smart Crossword Generation**: Adaptive algorithm that creates well-connected puzzles with high fill percentages (65–75%)
- **Vinkelord (Bent Words)**: Supports L-shaped words that change direction at a bend cell, adding variety to the grid layout
- **Swedish Dictionary**: 100,000+ Swedish words with clues from Lexin, synonym pairs, the Kelly frequency list, DSSO, and a custom word file
- **Daily Puzzles**: Automated daily puzzle generation via GitHub Actions, deployed to GitHub Pages; word-analysis results are cached between runs for faster generation
- **Interactive Web Player**: Browser-based crossword player with:
  - Keyboard navigation (arrow keys, space to toggle direction, Tab/Shift+Tab between clues)
  - Progress tracking and timer
  - Shared leaderboard with medal podium for top 3 (via Cloudflare Workers + JSONBin.io)
  - Historical leaderboard showing top scores from the past 30 days
  - Mobile-responsive design (portrait and landscape modes)
- **Anti-cheat System**: Validates puzzle completion times, input patterns, DevTools detection, and solution-view tracking via Cloudflare KV
- **Bonus Words**: Detects valid accidental words formed during generation and includes them as extra clues
- **Clue Handler Tool**: Standalone CLI for managing the dictionary — view statistics, add words, edit clues, auto-populate clues from Wiktionary, and generate compound/pattern-based clues
- **Word Manager Tool**: Lightweight CLI for quickly adding words, converting plain-text word lists to JSON, and merging word files
- **SEO Optimized**: Structured data, sitemap, robots.txt for search engine visibility

## Project Structure

```
SwedishCrosswords/
|-- SwedishCrossword/              # Main generator application
|   |-- Data/                      # Dictionary data files
|   |   |-- lexin-words.json       # Lexin dictionary (imported)
|   |   |-- synonym-words.json     # Synonym pairs (imported)
|   |   |-- kelly-words.json       # Kelly word list (imported)
|   |   |-- kelly-clues.json       # Curated clue overrides for Kelly words
|   |   |-- dsso-words.json        # DSSO dictionary (imported from source file)
|   |   |-- custom-words.json      # Custom/hand-curated words loaded at runtime
|   |   |-- lexin-swe-swe.xml      # Source Lexin XML
|   |   |-- synpairs.xml           # Source synonym pairs XML
|   |   |-- kelly.xml              # Kelly frequency word list (source XML)
|   |   +-- dsso-1.51.txt          # DSSO source file (dsso-1.51)
|   |-- Models/                    # Domain models
|   |   |-- Word.cs                # Word with clue, metadata, and segments
|   |   |-- WordSegment.cs         # Segment of a bent word path
|   |   |-- CrosswordGrid.cs       # Grid state, placement, and validation
|   |   |-- GridCell.cs            # Individual cell data
|   |   +-- AccidentalWord.cs      # Bonus word detection
|   |-- Services/                  # Core services
|   |   |-- CrosswordGenerator.cs  # Main generation orchestrator
|   |   |-- SwedishDictionary.cs   # Word lookup and filtering (loads all 5 sources)
|   |   |-- GridValidator.cs       # Puzzle validation
|   |   |-- PrintService.cs        # Output formatting (JSON, text)
|   |   |-- ClueGenerator.cs       # Clue generation
|   |   |-- LexinWordImporter.cs   # Lexin XML parser
|   |   |-- SynonymPairImporter.cs # Synonym XML parser
|   |   |-- KellyWordImporter.cs   # Kelly word list importer
|   |   |-- DssoWordImporter.cs    # DSSO source file parser
|   |   |-- DataDirectory.cs       # Data file path resolution
|   |   +-- Generation/            # Generation sub-components
|   |       |-- WordPlacer.cs      # Anchor selection and adaptive placement
|   |       |-- WordAnalyzer.cs    # Connectivity scoring with disk cache
|   |       |-- GapFiller.cs       # Gap and bridge filling strategies
|   |       |-- VinkelordPlacer.cs # Bent word opportunity detection
|   |       |-- GenerationHelpers.cs # Shared utility functions
|   |       +-- GenerationModels.cs  # Internal generation models
|   |-- wwwroot/                   # Web assets (deployed to GitHub Pages)
|   |   |-- index.html             # Main crossword player
|   |   |-- site.js                # Game logic, navigation, and leaderboard
|   |   |-- site.min.css           # Responsive styles
|   |   |-- om-oss.html            # About page
|   |   |-- kontakt.html           # Contact page
|   |   |-- integritetspolicy.html # Privacy policy
|   |   |-- sitemap.xml            # SEO sitemap
|   |   |-- robots.txt             # Crawler directives
|   |   |-- ads.txt                # Google AdSense verification
|   |   |-- site.webmanifest       # PWA manifest
|   |   |-- favicon.ico            # Favicon
|   |   |-- favicon-16x16.png      # 16×16 favicon
|   |   |-- favicon-32x32.png      # 32×32 favicon
|   |   |-- apple-touch-icon.png   # Apple touch icon
|   |   |-- android-chrome-*.png   # Android PWA icons (192×192, 512×512)
|   |   +-- CNAME                  # Custom domain config
|   +-- Program.cs                 # CLI entry point
|-- ClueHandler/                   # Dictionary management tool
|   |-- Program.cs                 # CLI: statistics, add words, edit clues, Wiktionary lookup
|   |-- WiktionaryClueService.cs   # Auto-populate clues from Swedish Wiktionary dump
|   |-- CompoundClueGenerator.cs   # Generate clues for compound words via DSSO metadata
|   +-- PatternClueGenerator.cs    # Generate clues using morphological patterns
|-- WordManager/                   # Lightweight word-management helper (no .csproj yet)
|   +-- Program.cs                 # CLI: quick-add words, convert text?JSON, merge files
|-- SwedishCrossword.Tests/        # TUnit test project
|   |-- GridCellTests.cs           # Grid cell model tests
|   |-- WordTests.cs               # Word model tests
|   |-- CrosswordGridTests.cs      # Grid functionality tests
|   |-- SwedishDictionaryTests.cs  # Dictionary validation tests
|   |-- GridValidatorTests.cs      # Puzzle validation tests
|   |-- AccidentalWordTests.cs     # Bonus word detection tests
|   |-- VinkelordTests.cs          # Bent word placement tests
|   |-- VinkelordIntertwiningTests.cs # Intertwined vinkelord edge-case tests
|   +-- PrintServiceTests.cs       # Output formatting tests
|-- infrastructure/                # Infrastructure-as-code
|   +-- cloudflare-worker/         # Cloudflare Worker for leaderboard proxy
|       |-- worker.js              # Worker source (leaderboard CRUD, history, solution-view tracking)
|       +-- README.md              # Endpoints, env vars, KV setup, and deployment guide
+-- .github/workflows/             # GitHub Actions
    +-- daily-crossword.yml        # Daily puzzle generation, tests & deployment
```

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Running the Generator

```bash
# Clone the repository
git clone https://github.com/eoq746/SwedishCrossword.git
cd SwedishCrossword

# Run the generator
dotnet run --project SwedishCrossword
```

### Menu Options

1. **Generate Easy Crossword (11x11)** - Quick puzzles
2. **Generate Medium Crossword (15x15)** - Standard puzzles
3. **Generate Hard Crossword (19x19)** - Challenging puzzles
4. **Show Dictionary Statistics** - Word count, categories, lengths
5. **Import from Lexin** - Download and parse Lexin dictionary
6. **Import Synonym Pairs** - Parse Folkets synonymlexikon
7. **Import Kelly Words** - Parse the Kelly frequency word list
8. **Generate for Web** - Creates puzzle.json and starts local server
9. **Import from DSSO** - Parse Den Stora Svenska Ordlistan source file

### Headless Generation (CI/CD)

```bash
dotnet run --project SwedishCrossword -- --generate-for-web
```

This mode is used by GitHub Actions for automated daily generation. The word-analysis cache is stored under `SwedishCrossword/.cache` and is preserved between runs via `actions/cache`.

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

### WordManager

> ?? Work-in-progress — no `.csproj` yet; references `SwedishCrossword.Services` directly.

Lightweight helper for bulk word management:

1. **Add words quickly** — Enter words in `WORD|Clue|Category|Difficulty` format
2. **Convert text to JSON** — Transform a plain-text word list into the JSON schema used by `SwedishDictionary`
3. **Merge word files** — Combine multiple word-list JSON files, deduplicating by word text

## Running Tests

```bash
dotnet test SwedishCrossword.Tests
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
- Rollback-on-failure: each placement is tested against a full grid backup and reverted if it creates invalid words

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

- **Hosting**: GitHub Pages (static files)
- **Daily Generation**: GitHub Actions (scheduled at midnight UTC); tests run before generation; word-analysis scores are cached between runs using `actions/cache` keyed on the dictionary file hashes
- **Leaderboard**: Cloudflare Workers proxy to JSONBin.io; top 3 entries shown with medal podium (??????); historical scores stored in Cloudflare KV (90-day TTL)
- **Solution-View Tracking**: Cloudflare KV records per-IP solution views (7-day TTL) so the anti-cheat system can flag players who viewed the answer before submitting
- **Infrastructure**: Worker source and docs live under `infrastructure/cloudflare-worker/`
- **Analytics**: Google AdSense (optional)

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
- [JSONBin.io](https://jsonbin.io) for leaderboard storage
- [Cloudflare Workers](https://workers.cloudflare.com) for API proxy