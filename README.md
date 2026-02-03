# Svenskt Korsord (Swedish Crossword)

[![Daily Crossword Generation](https://github.com/eoq746/SwedishCrossword/actions/workflows/daily-crossword.yml/badge.svg)](https://github.com/eoq746/SwedishCrossword/actions/workflows/daily-crossword.yml)

A Swedish crossword puzzle generator and web player. Generates high-quality crossword puzzles using a Swedish dictionary based on [Lexin (ISOF)](https://spraakbanken.gu.se/resurser/lexin) and [Folkets synonymlexikon](http://lexikon.nada.kth.se/synlex.html).

**Play the daily puzzle:** [svensktkorsord.se](https://svensktkorsord.se)

## Features

- **Smart Crossword Generation**: Adaptive algorithm that creates well-connected puzzles with high fill percentages (65-75%)
- **Swedish Dictionary**: 50,000+ Swedish words with clues from Lexin and synonym pairs
- **Daily Puzzles**: Automated daily puzzle generation via GitHub Actions, deployed to GitHub Pages
- **Interactive Web Player**: Browser-based crossword player with:
  - Keyboard navigation (arrow keys, space to toggle direction)
  - Progress tracking and timer
  - Shared leaderboard (via Cloudflare Workers + JSONBin.io)
  - Mobile-responsive design (portrait and landscape modes)
- **Anti-cheat System**: Validates puzzle completion times, input patterns, and DevTools detection
- **Bonus Words**: Detects valid accidental words formed during generation
- **SEO Optimized**: Structured data, sitemap, robots.txt for search engine visibility

## Project Structure

```
SwedishCrosswords/
|-- SwedishCrossword/              # Main generator application
|   |-- Data/                      # Dictionary data files
|   |   |-- lexin-words.json       # Lexin dictionary (imported)
|   |   |-- synonym-words.json     # Synonym pairs (imported)
|   |   |-- lexin-swe-swe.xml      # Source Lexin XML
|   |   +-- synpairs.xml           # Source synonym pairs XML
|   |-- Models/                    # Domain models
|   |   |-- Word.cs                # Word with clue and metadata
|   |   |-- CrosswordGrid.cs       # Grid state and statistics
|   |   |-- GridCell.cs            # Individual cell data
|   |   +-- AccidentalWord.cs      # Bonus word detection
|   |-- Services/                  # Core services
|   |   |-- CrosswordGenerator.cs  # Main generation algorithm
|   |   |-- SwedishDictionary.cs   # Word lookup and filtering
|   |   |-- GridValidator.cs       # Puzzle validation
|   |   |-- PrintService.cs        # Output formatting (JSON, text)
|   |   |-- ClueGenerator.cs       # Clue generation
|   |   |-- LexinWordImporter.cs   # Lexin XML parser
|   |   +-- SynonymPairImporter.cs # Synonym XML parser
|   |-- wwwroot/                   # Web assets (deployed to GitHub Pages)
|   |   |-- index.html             # Main crossword player
|   |   |-- site.js                # Game logic and leaderboard
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
|-- SwedishCrossword.Tests/        # TUnit test project
|   |-- GridCellTests.cs           # Grid cell model tests
|   |-- WordTests.cs               # Word model tests
|   |-- CrosswordGridTests.cs      # Grid functionality tests
|   |-- SwedishDictionaryTests.cs  # Dictionary validation tests
|   |-- GridValidatorTests.cs      # Puzzle validation tests
|   |-- AccidentalWordTests.cs     # Bonus word detection tests
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
7. **Generate for Web** - Creates puzzle.json and starts local server

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
- Print service output tests

## Algorithm Highlights

### Word Selection
- Prioritizes words with common Swedish letters (A, E, R, S, T, N)
- Balances across and down word placement
- Uses intersection potential scoring for anchor words

### Validation
- Ensures all words are connected (no isolated words)
- Prevents duplicate word usage
- Validates accidental word formations against dictionary

### Quality Metrics
- Target fill percentage: 65%+
- Minimum word count based on grid size
- Proper word isolation (no unintended adjacencies)

## Dictionary Sources

### Lexin (Primary)
The main dictionary is sourced from [Lexin](https://spraakbanken.gu.se/resurser/lexin), a Swedish-foreign language lexicon maintained by ISOF (Institute for Language and Folklore).

### Folkets synonymlexikon (Synonyms)
Additional synonym pairs from [Folkets synonymlexikon](http://lexikon.nada.kth.se/synlex.html), providing word-to-synonym clues.

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
- [JSONBin.io](https://jsonbin.io) for leaderboard storage
- [Cloudflare Workers](https://workers.cloudflare.com) for API proxy