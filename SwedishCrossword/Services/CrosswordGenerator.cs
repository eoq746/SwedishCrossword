using SwedishCrossword.Models;
using SwedishCrossword.Services.Generation;

namespace SwedishCrossword.Services;

/// <summary>
/// Main service for generating Swedish crossword puzzles with advanced placement strategies.
/// Delegates to helper classes for word analysis, gap filling, bridge filling, vinkelord, and word placement.
/// </summary>
public class CrosswordGenerator
{
    private readonly SwedishDictionary _dictionary;
    private readonly GridValidator _validator;
    private readonly Random _random;
    private readonly WordAnalyzer _wordAnalyzer = new();
    private readonly GapFiller _gapFiller;
    private readonly VinkelordPlacer _vinkelordPlacer;
    private readonly WordPlacer _wordPlacer;

    public CrosswordGenerator(SwedishDictionary dictionary, GridValidator validator)
    {
        _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        // Use a more random seed combining time with a unique value
        _random = new Random(Guid.NewGuid().GetHashCode());
        _gapFiller = new GapFiller(dictionary, _random);
        _vinkelordPlacer = new VinkelordPlacer(dictionary, _random);
        _wordPlacer = new WordPlacer(dictionary, _random, _vinkelordPlacer);
    }

    /// <summary>
    /// Generates a crossword puzzle with the specified parameters
    /// </summary>
    public async Task<CrosswordPuzzle> GenerateAsync(CrosswordGenerationOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var attempts = 0;
        var maxAttempts = options.MaxAttempts;
        var validationRejections = 0;

        // Pre-compute candidate words and their analysis ONCE outside the retry loop
        var candidateWords = GetCandidateWords(options).ToList();
        if (candidateWords.Count == 0)
        {
            throw new InvalidOperationException("No suitable words found for the specified criteria");
        }

        // Analyze word connectivity once - this is expensive and results don't change between attempts
        var wordAnalysis = _wordAnalyzer.AnalyzeWordConnectivity(candidateWords);
        var sortedAnalysis = wordAnalysis
            .OrderByDescending(w => w.ConnectivityScore)
            .ThenBy(w => w.Word.Length)
            .ToList();

        while (attempts < maxAttempts && !cancellationToken.IsCancellationRequested)
        {
            attempts++;

            try
            {
                var grid = new CrosswordGrid(options.Width, options.Height);
                var result = await TryGenerateSmartPuzzleAsync(grid, candidateWords, sortedAnalysis, options, cancellationToken);

                if (result != null)
                {
                    Console.WriteLine($"Korsord genererat efter {attempts} försök ({result.GetStats().FillPercentage:F1}% fyllnad)");
                    if (validationRejections > 0)
                    {
                        Console.WriteLine($"    {validationRejections} korsord avvisades vid validering under generering");
                    }
                    return new CrosswordPuzzle(result, attempts, _dictionary);
                }
                else if (grid.Words.Any())
                {
                    validationRejections++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($" Försök {attempts} misslyckades: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"    Inre fel: {ex.InnerException.Message}");
                }
                if (ex is not InvalidOperationException)
                {
                    Console.WriteLine($"    Stack: {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
                }
            }

            if (attempts % 50 == 0 || (attempts > 20 && attempts % 25 == 0 && validationRejections > attempts * 0.8))
            {
                Console.WriteLine($" Försök {attempts}/{maxAttempts}... ({validationRejections} avvisade vid validering, {(double)validationRejections/attempts*100:F0}% avvisningsfrekvens)");
            }

            await Task.Yield();
        }

        var rejectionRate = (double)validationRejections / attempts * 100;
        var message = validationRejections > 0
            ? $" Kunde inte generera giltigt korsord efter {maxAttempts} försök.\n" +
              $"    {validationRejections} av {attempts} försök avvisades vid validering ({rejectionRate:F1}% avvisningsfrekvens)\n" +
              $"    Hög avvisningsfrekvens kan indikera för strikta valideringsregler eller för liten ordlista"
            : $" Kunde inte generera korsord efter {maxAttempts} försök\n" +
              $"    Inga ord kunde placeras - kontrollera ordlista och generationsalternativ";

        throw new InvalidOperationException(message);
    }

    private async Task<CrosswordGrid?> TryGenerateSmartPuzzleAsync(CrosswordGrid grid, List<Word> candidateWords,
        List<WordAnalysis> sortedAnalysis, CrosswordGenerationOptions options, CancellationToken cancellationToken)
    {
        // Create a shuffled copy using top-biased Fisher-Yates
        var sortedWords = sortedAnalysis.ConvertAll(a => a.Word);
        GenerationHelpers.ShuffleTopBiased(sortedWords, 5, _random);

        // Track placed words with their scores for debugging
        var placedWordScores = new Dictionary<string, double>();

        // Pre-computed connectivity scores from WordAnalysis — used as the single
        // consistent score stored in placedWordScores by all placement strategies.
        var connectivityScores = new Dictionary<string, double>(sortedAnalysis.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var wa in sortedAnalysis)
            connectivityScores[wa.Word.Text] = wa.ConnectivityScore;

        // Phase 1: Smart anchor word selection with randomness
        if (!_wordPlacer.PlaceAnchorWordsWithValidation(grid, sortedWords, candidateWords, options))
        {
            return null;
        }
        Console.WriteLine($"Fas 1: {grid.GetStats().FillPercentage:F1}% fyllnad");

        var placedWords = grid.Words.ToHashSet();
        var adaptiveState = _wordPlacer.CreateAdaptiveState(options, placedWords, grid);

        // Derive bridge length limit from grid dimensions
        var maxBridgeLength = Math.Max(grid.Width, grid.Height) - 2;

        // Phase 2: Unified interleaved fill loop.
        // Each cycle runs all strategies in sequence: adaptive placement (with
        // integrated vinkelord) → gap/bridge filling. Gap/bridge filling creates
        // new intersection opportunities that adaptive placement can exploit in the
        // next cycle, and adaptive placement opens new gap/bridge/vinkelord
        // opportunities in return.
        for (int cycle = 1; cycle <= 25; cycle++)
        {
            Console.WriteLine($"Cykel {cycle}: start - {grid.GetStats().FilledCells} fyllda celler, {grid.Words.Count} ord placerade");
            var cycleStart = grid.GetStats().FilledCells;

            // Sub-phase A: Adaptive word placement with integrated vinkelord (bounded batch)
            // Fully reset adaptive state each cycle — other strategies (gaps, bridges)
            // create new intersection opportunities that the adaptive placer can now
            // exploit. Without this, the cumulative PlacementAttempts budget causes
            // permanent exhaustion after a few cycles.
            adaptiveState.IsExhausted = false;
            adaptiveState.PlacementAttempts = 0;
            adaptiveState.CurrentTargetLength = options.MaxWordLength;
            adaptiveState.UsedWordTexts = grid.GetPlacedWordTexts();
            adaptiveState.UsedWordsRefreshCounter = 0;
            adaptiveState.ConsecutiveFailures = 0;
            adaptiveState.TriedWords.Clear();

            await _wordPlacer.PlaceWordsAdaptivelyWithValidation(
                grid, sortedWords, placedWords, options, placedWordScores, connectivityScores, cancellationToken, adaptiveState);
            Console.WriteLine($"Fas 2 subfas A: {grid.GetStats().FillPercentage:F1}% fyllnad");

            // Sub-phase B: Gap/bridge filling (multi-pass within cycle)
            // Scans rows and columns for patterns with existing letters and empty
            // cells between them, then finds dictionary words matching those patterns.
            // Handles all gap sizes — from single-cell gaps needing 3-letter words
            // to longer bridges spanning multiple empty cells.
            for (int pass = 1; pass <= 5; pass++)
            {
                var before = grid.GetStats().FilledCells;
                await _gapFiller.FillBridgeOpportunitiesAsync(grid, candidateWords, placedWords, options, placedWordScores, connectivityScores, maxBridgeLength, cancellationToken);
                var wordsPlacedThisPass = grid.GetStats().FilledCells - before;
                if (wordsPlacedThisPass == 0) break;
            }
            Console.WriteLine($"Fas 2 subfas B: {grid.GetStats().FillPercentage:F1}% fyllnad");

            var cycleEnd = grid.GetStats().FilledCells;
            if (cycleEnd == cycleStart) break;
        }

        // Validation
        var stats = grid.GetStats();
        var minWords = Math.Max(3, grid.Width / 4);

        if (placedWords.Count < minWords)
        {
            Console.WriteLine($"  Avvisad: för få ord ({placedWords.Count} < {minWords})");
            return null;
        }

        if (stats.FillPercentage < options.TargetFillPercentage)
        {
            Console.WriteLine($"  Avvisad: för låg fyllnad ({stats.FillPercentage:F1}% < {options.TargetFillPercentage:F1}%)");
            return null;
        }

        if (!_validator.IsValidCrossword(grid))
        {
            Console.WriteLine($"  Avvisad: ogiltig korsordstruktur");
            return null;
        }

        var validation = grid.ValidateCrossword(_dictionary);

        if (options.RejectInvalidWords && validation.InvalidAccidentalWords.Any())
        {
            Console.WriteLine($"  Avvisad: {validation.InvalidAccidentalWords.Count} ogiltiga ord: {string.Join(", ", validation.InvalidAccidentalWords.Select(w => w.Text))}");
            return null;
        }

        ReportGenerationResults(validation, grid.Words.Count, grid, placedWordScores);
        grid.FillEmptyCellsWithAsterisks();

        return grid;
    }

    private void ReportGenerationResults(CrosswordValidationResult validation, int usedWordCount, CrosswordGrid grid, Dictionary<string, double> placedWordScores)
    {
        if (validation.ValidAccidentalWords.Any())
        {
            Console.WriteLine($"Bonus: {validation.ValidAccidentalWords.Count} giltiga svenska bonusord hittades");
        }

        if (validation.InvalidAccidentalWords.Any())
        {
            Console.WriteLine($"KRITISKT: {validation.InvalidAccidentalWords.Count} ogiltiga ord hittades");
        }

        Console.WriteLine($"Använda ord: {usedWordCount}");

        var vinkelordCount = grid.Words.Count(w => w.IsBent);
        if (vinkelordCount > 0)
        {
            Console.WriteLine($"Vinkelord: {vinkelordCount} böjda ord placerade");
        }

        Console.WriteLine("Fördelning per längd:");
        var groups = placedWordScores
            .GroupBy(kvp => kvp.Key.Length)
            .OrderBy(g => g.Key)
            .Select(g => (Length: g.Key, Count: g.Count()))
            .ToList();

        if (groups.Count > 0)
        {
            var maxCount = groups.Max(g => g.Count);
            const int maxBarWidth = 40;

            foreach (var (length, count) in groups)
            {
                var barWidth = (int)Math.Ceiling((double)count / maxCount * maxBarWidth);
                var bar = new string('#', barWidth);
                Console.WriteLine($"  {length,2} bokstäver: {count,3} ord {bar}");
            }
        }
    }

    private IEnumerable<Word> GetCandidateWords(CrosswordGenerationOptions options)
    {
        var words = _dictionary.GetWords(
            minLength: options.MinWordLength,
            maxLength: options.MaxWordLength,
            difficulty: options.Difficulty
        );

        if (options.Categories != null && options.Categories.Count > 0)
        {
            words = words.Where(w => options.Categories.Contains(w.Category, StringComparer.OrdinalIgnoreCase));
        }

        return words.ToList();
    }
}

/// <summary>
/// Configuration options for crossword generation
/// </summary>
public class CrosswordGenerationOptions
{
    public int Width { get; set; } = 15;
    public int Height { get; set; } = 15;
    public int MinWordLength { get; set; } = 1;
    public int MaxWordLength { get; set; } = 12;
    public double TargetFillPercentage { get; set; } = 45.0;
    public DifficultyLevel? Difficulty { get; set; }
    public List<string>? Categories { get; set; }
    public int MaxAttempts { get; set; } = 100;
    public bool RejectInvalidWords { get; set; } = true;

    /// <summary>Whether to allow vinkelord (bent/angled words) during generation</summary>
    public bool AllowVinkelord { get; set; } = true;

    /// <summary>Maximum number of vinkelord to place per puzzle</summary>
    public int MaxVinkelord { get; set; } = Int32.MaxValue;

    /// <summary>Maximum number of bends allowed per word (1 = L-shape, 2 = Z/S-shape, etc.)</summary>
    public int MaxBendsPerWord { get; set; } = 1;

    /// <summary>
    /// Maximum word length for vinkelord, computed from grid dimensions.
    /// A word with N bends can theoretically span more cells than a single dimension.
    /// </summary>
    public int MaxVinkelordLength => Width + Height - 1;

    public static CrosswordGenerationOptions Easy => new()
    {
        Width = 11,
        Height = 11,
        MinWordLength = 1,
        MaxWordLength = 11,
        TargetFillPercentage = 45.0,
        Difficulty = null,
        MaxAttempts = 50,
        RejectInvalidWords = true
    };

    public static CrosswordGenerationOptions Medium => new()
    {
        Width = 15,
        Height = 15,
        MinWordLength = 1,
        MaxWordLength = 15,
        TargetFillPercentage = 65.0,
        Difficulty = null,
        MaxAttempts = 80,
        RejectInvalidWords = true
    };

    public static CrosswordGenerationOptions Hard => new()
    {
        Width = 17,
        Height = 17,
        MinWordLength = 1,
        MaxWordLength = 17,
        TargetFillPercentage = 70.0,
        Difficulty = null,
        MaxAttempts = 120,
        RejectInvalidWords = true,
        AllowVinkelord = true
    };

    public static CrosswordGenerationOptions Small => new()
    {
        Width = 9,
        Height = 9,
        MinWordLength = 1,
        MaxWordLength = 9,
        TargetFillPercentage = 45.0,
        Difficulty = null,
        MaxAttempts = 30,
        RejectInvalidWords = true
    };
}

/// <summary>
/// Represents a completed crossword puzzle
/// </summary>
public class CrosswordPuzzle
{
    public CrosswordGrid Grid { get; }
    public DateTime CreatedAt { get; }
    public int GenerationAttempts { get; }
    public GridStats Statistics { get; }
    public CrosswordValidationResult ValidationResult { get; set; }

    public CrosswordPuzzle(CrosswordGrid grid, int attempts, SwedishDictionary? dictionary = null)
    {
        Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        GenerationAttempts = attempts;
        CreatedAt = DateTime.Now;
        Statistics = grid.GetStats();

        // Validate with dictionary if provided - this will detect accidental words,
        // mark valid ones for inclusion, and assign proper PuzzleNumber values
        ValidationResult = Grid.ValidateCrossword(dictionary);
    }

    public (List<Word> Across, List<Word> Down) GetClues()
    {
        return Grid.GetWordsByDirection();
    }

}