# Svenskt Korsord (Swedish Crossword)

[![Daily Crossword Generation](https://github.com/eoq746/SwedishCrossword/actions/workflows/daily-crossword.yml/badge.svg)](https://github.com/eoq746/SwedishCrossword/actions/workflows/daily-crossword.yml)

A Swedish crossword puzzle generator and web player. Generates high-quality crossword puzzles using a Swedish dictionary based on [Lexin (ISOF)](https://spraakbanken.gu.se/resurser/lexin).

?? **Play the daily puzzle:** [svensktkorsord.se](https://svensktkorsord.se)

## Features

- **Smart Crossword Generation**: Adaptive algorithm that creates well-connected puzzles with high fill percentages (65-75%)
- **Swedish Dictionary**: 6,400+ Swedish words with clues, categories, and difficulty levels
- **Daily Puzzles**: Automated daily puzzle generation via GitHub Actions
- **Interactive Web Player**: Browser-based crossword player with:
  - Keyboard navigation (arrow keys, space to toggle direction)
  - Progress tracking and timer
  - Shared leaderboard
  - Mobile-responsive design
- **Anti-cheat System**: Validates puzzle completion times and input patterns
- **Bonus Words**: Detects valid accidental words formed during generation

## Project Structure

```
SwedishCrosswords/
??? SwedishCrossword/           # Main generator application
?   ??? Data/                   # Dictionary data (lexin-words.json)
?   ??? Models/                 # Domain models (Word, CrosswordGrid, etc.)
?   ??? Services/               # Core services
?   ?   ??? CrosswordGenerator.cs   # Main generation algorithm
?   ?   ??? SwedishDictionary.cs    # Word lookup and filtering
?   ?   ??? GridValidator.cs        # Puzzle validation
?   ?   ??? PrintService.cs         # Output formatting
?   ??? wwwroot/                # Web assets (deployed to GitHub Pages)
?       ??? index.html          # Main crossword player
?       ??? puzzle.json         # Generated puzzle data
??? SwedishCrossword.Tests/     # TUnit test project
??? .github/workflows/          # GitHub Actions for daily generation
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

1. **Generate Easy Crossword (11×11)** - Quick puzzles
2. **Generate Medium Crossword (15×15)** - Standard puzzles
3. **Generate Hard Crossword (19×19)** - Challenging puzzles
4. **Show Dictionary Statistics** - Word count, categories, lengths
5. **Import from Lexin** - Download and parse Lexin dictionary
6. **Generate for Web** - Creates puzzle.json and standalone HTML

### Headless Generation (CI/CD)

```bash
dotnet run --project SwedishCrossword -- --generate-for-web
```

## Running Tests

```bash
dotnet test SwedishCrossword.Tests
```

The test suite includes:
- Dictionary loading and validation tests
- Grid placement and connectivity tests
- Swedish character handling tests
- Word duplication prevention tests
- Fill percentage benchmarks

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

## Dictionary

The dictionary is sourced from [Lexin](https://spraakbanken.gu.se/resurser/lexin), a Swedish-foreign language lexicon maintained by ISOF (Institute for Language and Folklore).

**Statistics:**
- ~6,500 words
- Categories: Substantiv, Verb, Adjektiv, Adverb, etc.
- Difficulty levels: Easy, Medium, Hard
- Full support for Swedish characters (Å, Ä, Ö)

## License

The dictionary data is licensed under [Creative Commons Attribution 2.5 Sweden](https://creativecommons.org/licenses/by/2.5/se/).

## Contributing

Contributions are welcome! Please feel free to submit issues or pull requests.

### Areas for Improvement
- Additional word sources
- Themed puzzle generation
- Difficulty-based word selection
- Mobile app version

## Acknowledgments

- [Lexin/ISOF](https://spraakbanken.gu.se/resurser/lexin) for the Swedish dictionary
- [JSONBin.io](https://jsonbin.io) for leaderboard storage