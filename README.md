# Svenskt Korsord (Swedish Crossword)

[![Daily Crossword Generation](https://github.com/eoq746/SwedishCrossword/actions/workflows/daily-crossword.yml/badge.svg)](https://github.com/eoq746/SwedishCrossword/actions/workflows/daily-crossword.yml)

A Swedish crossword puzzle generator and web player. Generates high-quality crossword puzzles using a Swedish dictionary based on [Lexin (ISOF)](https://spraakbanken.gu.se/resurser/lexin), [Folkets synonymlexikon](http://lexikon.nada.kth.se/synlex.html), and the [Kelly word list](https://spraakbanken.gu.se/resurser/kelly).

**Play the daily puzzle:** [svensktkorsord.se](https://svensktkorsord.se)

## Features

- **Smart Crossword Generation**: Adaptive algorithm that creates well-connected puzzles with high fill percentages (65–75%)
- **Vinkelord (Bent Words)**: Supports L-shaped words that change direction at a bend cell, adding variety to the grid layout
- **Swedish Dictionary**: 50,000+ Swedish words with clues from Lexin, synonym pairs, and the Kelly frequency list
- **Daily Puzzles**: Automated daily puzzle generation via GitHub Actions, deployed to GitHub Pages
- **Interactive Web Player**: Browser-based crossword player with:
  - Keyboard navigation (arrow keys, space to toggle direction, Tab/Shift+Tab between clues)
  - Progress tracking and timer
  - Shared leaderboard (via Cloudflare Workers + JSONBin.io)
  - Mobile-responsive design (portrait and landscape modes)
- **Anti-cheat System**: Validates puzzle completion times, input patterns, and DevTools detection
- **Bonus Words**: Detects valid accidental words formed during generation and includes them as extra clues
- **Clue Handler Tool**: Standalone CLI for managing the dictionary — view statistics, add words, and edit clues
- **SEO Optimized**: Structured data, sitemap, robots.txt for search engine visibility

## Project Structure

```
SwedishCrosswords/
|-- SwedishCrossword/              # Main generator application
|   |-- Data/                      # Dictionary data files
|   |   |-- lexin-words.json       # Lexin dictionary (imported)
|   |   |-- synonym-words.json     # Synonym pairs (imported)
|   |   |-- lexin-swe-swe.xml      # Source Lexin XML
|   |   |-- synpairs.xml           # Source synonym pairs XML
|   |   +-- kelly.xml              # Kelly frequency word list
|   |-- Models/                    # Domain models
|   |   |-- Word.cs                # Word with clue, metadata, and segments
|   |   |-- WordSegment.cs         # Segment of a bent word path
|   |   |-- CrosswordGrid.cs       # Grid state, placement, and validation
|   |   |-- GridCell.cs            # Individual cell data
|   |   +-- AccidentalWord.cs      # Bonus word detection
|   |-- Services/                  # Core services
|   |   |-- CrosswordGenerator.cs  # Main generation orchestrator
|   |   |-- SwedishDictionary.cs   # Word lookup and filtering
|   |   |-- GridValidator.cs       # Puzzle validation
|   |   |-- PrintService.cs        # Output formatting (JSON, text)
|   |   |-- ClueGenerator.cs       # Clue generation
|   |   |-- LexinWordImporter.cs   # Lexin XML parser
|   |   |-- SynonymPairImporter.cs # Synonym XML parser
|   |   |-- KellyWordImporter.cs   # Kelly word list importer
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
|   |   +-- CNAME                  # Custom domain config
|   +-- Program.cs                 # CLI entry point
|-- ClueHandler/                   # Dictionary management tool
|   +-- Program.cs                 # CLI for stats, adding words, editing clues
|-- SwedishCrossword.Tests/        # TUnit test project
|   |-- GridCellTests.cs           # Grid cell model tests
|   |-- WordTests.cs               # Word model tests
|   |-- CrosswordGridTests.cs      # Grid functionality tests
|   |-- SwedishDictionaryTests.cs  # Dictionary validation tests
|   |-- GridValidatorTests.cs      # Puzzle validation tests
|   |-- AccidentalWordTests.cs     # Bonus word detection tests
|   |-- VinkelordTests.cs          # Bent word placement tests
|   +-- PrintServiceTests.cs       # Output formatting tests
+-- .github/workflows/             # GitHub Actions
    +-- daily-crossword.yml        # Daily puzzle generation & deployment
```

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (Preview)

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

### Headless Generation (CI/CD)

```bash
dotnet run --project SwedishCrossword -- --generate-for-web
```

This mode is used by GitHub Actions for automated daily generation.

## Running Tests

```bash
dotnet test SwedishCrossword.Tests
```

The test suite includes:
- Grid cell and word model tests
- Grid placement and connectivity tests
- Swedish character handling tests (Å, Ä, Ö)
- Dictionary loading and validation tests
- Puzzle validation and bonus word tests
- Vinkelord (bent word) placement tests
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
Frequency-ranked vocabulary from the [Kelly project](https://spraakbanken.gu.se/resurser/kelly), categorized by CEFR level (A1–C2). Clues are generated from a curated clue dictionary with POS-based fallback patterns.

**Combined Statistics:**
- ~50,000+ words
- Categories: Substantiv, Verb, Adjektiv, Adverb, etc.
- Difficulty levels: Easy, Medium, Hard
- Full support for Swedish characters (Å, Ä, Ö)

## Web Architecture

- **Hosting**: GitHub Pages (static files)
- **Daily Generation**: GitHub Actions (scheduled at midnight UTC)
- **Leaderboard**: Cloudflare Workers proxy to JSONBin.io
- **Analytics**: Google AdSense (optional)

## License

The dictionary data is licensed under [Creative Commons Attribution 2.5 Sweden](https://creativecommons.org/licenses/by/2.5/se/).

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
- [JSONBin.io](https://jsonbin.io) for leaderboard storage
- [Cloudflare Workers](https://workers.cloudflare.com) for API proxy