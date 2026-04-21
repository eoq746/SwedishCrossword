using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly Random _random = Random.Shared;
    private readonly WordAnalyzer _wordAnalyzer = new();
    private readonly GapFiller _gapFiller;
    private readonly VinkelordPlacer _vinkelordPlacer;
    private readonly WordPlacer _wordPlacer;
    private readonly ILogger<CrosswordGenerator> _logger;

    public CrosswordGenerator(SwedishDictionary dictionary, GridValidator validator, ILogger<CrosswordGenerator> logger)
    {
        _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _logger = logger;
        _gapFiller = new GapFiller(dictionary, _random);
        _vinkelordPlacer = new VinkelordPlacer(dictionary, _random);
        _wordPlacer = new WordPlacer(dictionary, _random, _vinkelordPlacer);
    }

    public CrosswordGenerator(SwedishDictionary dictionary, GridValidator validator)
        : this(dictionary, validator, NullLogger<CrosswordGenerator>.Instance)
    {
    }

    /// <summary>
    /// Generates a crossword puzzle with the specified parameters
    /// </summary>
    public async Task<CrosswordPuzzle> GenerateAsync(CrosswordGenerationOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var maxAttempts = options.MaxAttempts;

        // Pre-compute candidate words and their analysis ONCE outside the retry loop
        var candidateWords = GetCandidateWords(options);
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

        // Pre-compute connectivity scores once — these never change between attempts
        var connectivityScores = sortedAnalysis.ToDictionary(
            wa => wa.Word.Text,
            wa => wa.ConnectivityScore,
            StringComparer.OrdinalIgnoreCase);

        var originalRejectDuplicateWords = options.RejectDuplicateWords;

        // Two-pass approach: first try with duplicate rejection, then relax it if needed
        var passCount = originalRejectDuplicateWords ? 2 : 1;
        for (int pass = 0; pass < passCount; pass++)
        {
            if (pass == 1)
            {
                _logger.LogInformation("Duplicate rejection prevents sufficient fill, relaxing constraint...");
                options.RejectDuplicateWords = false;
            }

            var attempts = 0;
            var validationRejections = 0;

            while (attempts < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                attempts++;

                try
                {
                    var grid = new CrosswordGrid(options.Width, options.Height);
                    var result = await TryGenerateSmartPuzzleAsync(grid, candidateWords, sortedAnalysis, connectivityScores, options, cancellationToken);

                    if (result != null)
                    {
                        var fillPercentage = result.GetStats().FillPercentage;
                        _logger.LogInformation("Crossword generated after {Attempts} attempts ({FillPercentage:F1}% fill)", attempts, fillPercentage);
                        if (pass > 0)
                        {
                            _logger.LogInformation("    (duplicate restriction relaxed)");
                        }
                        if (validationRejections > 0)
                        {
                            _logger.LogInformation("{ValidationRejections} crosswords rejected during validation", validationRejections);
                        }
                        options.RejectDuplicateWords = originalRejectDuplicateWords;
                        return new CrosswordPuzzle(result, attempts, _dictionary);
                    }
                    else if (grid.Words.Count > 0)
                    {
                        validationRejections++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Attempt {Attempt} failed", attempts);
                }

                if (attempts % 50 == 0 || (attempts > 20 && attempts % 25 == 0 && validationRejections > attempts * 0.8))
                {
                    var rejectionRate = (double)validationRejections / attempts * 100;
                    _logger.LogDebug("Attempt {Attempts}/{MaxAttempts}... ({Rejections} rejected, {Rate:F0}% rejection rate)", attempts, maxAttempts, validationRejections, rejectionRate);
                }

                await Task.Yield();
            }
        }

        options.RejectDuplicateWords = originalRejectDuplicateWords;

        throw new InvalidOperationException(
            $" Kunde inte generera giltigt korsord efter {maxAttempts} försök (per pass).\n" +
            $"    Kontrollera ordlista och generationsalternativ");
    }

    private async Task<CrosswordGrid?> TryGenerateSmartPuzzleAsync(CrosswordGrid grid, List<Word> candidateWords,
        List<WordAnalysis> sortedAnalysis, Dictionary<string, double> connectivityScores, CrosswordGenerationOptions options, CancellationToken cancellationToken)
    {
        // Suppress clue renumbering during generation — it's O(W*H) per placement
        // and completely unnecessary until the final validation pass.
        grid.SuppressRenumbering = true;

        // Create a shuffled copy using top-biased Fisher-Yates
        var sortedWords = sortedAnalysis.ConvertAll(a => a.Word);
        GenerationHelpers.ShuffleTopBiased(sortedWords, 5, _random);

        // Track placed words with their scores for debugging
        var placedWordScores = new Dictionary<string, double>();

        // Phase 1: Smart anchor word selection with randomness
        if (!_wordPlacer.PlaceAnchorWordsWithValidation(grid, sortedWords, candidateWords, options))
        {
            return null;
        }
        var phase1Stats = grid.GetStats();
        _logger.LogDebug("Phase 1: {FillPercentage:F1}% fill", phase1Stats.FillPercentage);

        var placedWords = grid.Words.ToHashSet();
        var adaptiveState = WordPlacer.CreateAdaptiveState(options, placedWords, grid);

        // Derive bridge length limit from grid dimensions
        var maxBridgeLength = Math.Max(grid.Width, grid.Height) - 2;

        // Phase 2: Unified interleaved fill loop.
        // Each cycle runs all strategies in sequence: adaptive placement (with
        // integrated vinkelord) → gap/bridge filling. Gap/bridge filling creates
        // new intersection opportunities that adaptive placement can exploit in the
        // next cycle, and adaptive placement opens new gap/bridge/vinkelord
        // opportunities in return.
        for (int cycle = 1; cycle <= 1; cycle++) // one cycle for now, can increase if needed
        {
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
                grid, sortedWords, placedWords, options, placedWordScores, connectivityScores, adaptiveState, 0, cancellationToken);
            var phase2AFillPercentage = grid.GetStats().FillPercentage;
            _logger.LogDebug("Phase 2A: {FillPercentage:F1}% fill", phase2AFillPercentage);

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
            var subBStats = grid.GetStats();
            _logger.LogDebug("Phase 2B: {FillPercentage:F1}% fill", subBStats.FillPercentage);

            var cycleEnd = subBStats.FilledCells;
            if (cycleEnd == cycleStart) break;
        }

        // Re-enable renumbering before final validation
        grid.SuppressRenumbering = false;

        // Validation
        var stats = grid.GetStats();
        var minWords = Math.Max(3, grid.Width / 4);

        if (placedWords.Count < minWords)
        {
            _logger.LogDebug("Rejected: too few words ({PlacedCount} < {MinWords})", placedWords.Count, minWords);
            return null;
        }

        if (stats.FillPercentage < options.TargetFillPercentage)
        {
            _logger.LogDebug("Rejected: fill too low ({FillPercentage:F1}% < {Target:F1}%)", stats.FillPercentage, options.TargetFillPercentage);
            return null;
        }

        if (!GridValidator.IsValidCrossword(grid))
        {
            _logger.LogDebug("Rejected: invalid crossword structure");
            return null;
        }

        var validation = grid.ValidateCrossword(_dictionary);

        if (options.RejectInvalidWords && validation.InvalidAccidentalWords.Count > 0)
        {
            var invalidWords = string.Join(", ", validation.InvalidAccidentalWords.Select(w => w.Text));
            _logger.LogDebug("Rejected: {Count} invalid words: {Words}", validation.InvalidAccidentalWords.Count, invalidWords);
            return null;
        }

        ReportGenerationResults(validation, grid.Words.Count, grid, placedWordScores);
        grid.FillEmptyCellsWithAsterisks();

        return grid;
    }

    private void ReportGenerationResults(CrosswordValidationResult validation, int usedWordCount, CrosswordGrid grid, Dictionary<string, double> placedWordScores)
    {
        if (validation.ValidAccidentalWords.Count > 0)
        {
            _logger.LogInformation("Bonus: {Count} valid Swedish bonus words found", validation.ValidAccidentalWords.Count);
        }

        if (validation.InvalidAccidentalWords.Count > 0)
        {
            _logger.LogWarning("CRITICAL: {Count} invalid words found", validation.InvalidAccidentalWords.Count);
        }

        _logger.LogInformation("Words used: {UsedWordCount}", usedWordCount);

        var vinkelordCount = grid.Words.Count(w => w.IsBent);
        if (vinkelordCount > 0)
        {
            _logger.LogInformation("Vinkelord: {VinkelordCount} bent words placed", vinkelordCount);
        }

        var groups = placedWordScores
            .GroupBy(kvp => kvp.Key.Length)
            .OrderBy(g => g.Key)
            .Select(g => (Length: g.Key, Count: g.Count()))
            .ToList();

        if (groups.Count > 0)
        {
            var distribution = string.Join(", ", groups.Select(g => $"{g.Length} letters: {g.Count}"));
            _logger.LogDebug("Length distribution: {Distribution}", distribution);
        }
    }

    private List<Word> GetCandidateWords(CrosswordGenerationOptions options)
    {
        var words = _dictionary.GetWords(
            minLength: options.MinWordLength,
            maxLength: options.MaxWordLength,
            difficulty: options.Difficulty
        );

        if (options.Categories is { Count: > 0 })
        {
            words = words.Where(w => options.Categories.Contains(w.Category, StringComparer.OrdinalIgnoreCase));
        }

        return [.. words];
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

    /// <summary>Whether to reject placing a word whose text already appears in the puzzle (including accidental words)</summary>
    public bool RejectDuplicateWords { get; set; } = true;

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
        RejectInvalidWords = true,
        RejectDuplicateWords = true
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
        RejectInvalidWords = true,
        RejectDuplicateWords = true
    };

    public static CrosswordGenerationOptions Hard => new()
    {
        Width = 17,
        Height = 17,
        MinWordLength = 1,
        MaxWordLength = 33,
        TargetFillPercentage = 70.0,
        Difficulty = null,
        MaxAttempts = 120,
        RejectInvalidWords = true,
        RejectDuplicateWords = true,
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
        RejectInvalidWords = true,
        RejectDuplicateWords = true
    };

    public static CrosswordGenerationOptions Mobile => new()
    {
        Width = 10,
        Height = 10,
        MinWordLength = 1,
        MaxWordLength = 10,
        TargetFillPercentage = 45.0,
        Difficulty = null,
        MaxAttempts = 50,
        RejectInvalidWords = true,
        RejectDuplicateWords = true
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
