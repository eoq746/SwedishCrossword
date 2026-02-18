using System.Security.Cryptography;
using System.Text;
using SwedishCrossword.Models;
using System.Text.Json;

namespace SwedishCrossword.Services;

/// <summary>
/// Main service for generating Swedish crossword puzzles with advanced placement strategies
/// </summary>
public class CrosswordGenerator
{
    private readonly SwedishDictionary _dictionary;
    private readonly GridValidator _validator;
    private readonly Random _random;

    // Cache for word analysis to avoid recomputing when word list hasn't changed
    private readonly object _analysisCacheLock = new();
    private string? _cachedWordsFingerprint;
    private List<WordAnalysis>? _cachedWordAnalysis;

    private const string CacheFileName = "wordAnalysisCache.json";

    private record WordAnalysisDto(string Text, double ConnectivityScore, int VowelCount, int CommonLetterCount);

    private record CacheFilePayload(string Fingerprint, List<WordAnalysisDto> Entries);

    public CrosswordGenerator(SwedishDictionary dictionary, GridValidator validator)
    {
        _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        // Use a more random seed combining time with a unique value
        _random = new Random(Guid.NewGuid().GetHashCode());
    }

    /// <summary>
    /// Performs a Fisher-Yates shuffle on the list in-place (O(n) instead of O(n²) RemoveAt approach)
    /// </summary>
    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            if (i != j)
            {
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }

    /// <summary>
    /// Partially shuffles a list by randomizing among the top 'topRange' elements at each position.
    /// Produces a biased-toward-top ordering similar to the original RemoveAt(pickIndex) pattern but in O(n).
    /// Only shuffles the first portion of the list to preserve tail ranking for small lists.
    /// </summary>
    private void ShuffleTopBiased<T>(List<T> list, int topRange)
    {
        // Only shuffle positions where we have enough remaining elements to make it meaningful.
        // For small lists, limit how far we shuffle to preserve tail ranking.
        var shuffleLimit = Math.Max(0, list.Count - topRange);
        for (int i = 0; i < shuffleLimit; i++)
        {
            int range = Math.Min(topRange, list.Count - i);
            int j = i + _random.Next(range);
            if (i != j)
            {
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
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
        var wordAnalysis = AnalyzeWordConnectivity(candidateWords);
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
                        Console.WriteLine($"    {validationRejections} korsord avvisades p.g.a. ogiltiga ord under generering");
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
                Console.WriteLine($" Försök {attempts}/{maxAttempts}... ({validationRejections} avvisade för ogiltiga ord, {(double)validationRejections/attempts*100:F0}% avvisningsfrekvens)");
            }

            await Task.Yield();
        }

        var rejectionRate = (double)validationRejections / attempts * 100;
        var message = validationRejections > 0 
            ? $" Kunde inte generera giltigt korsord efter {maxAttempts} försök.\n" +
              $"    {validationRejections} av {attempts} försök avvisades för ogiltiga ord ({rejectionRate:F1}% avvisningsfrekvens)\n" +
              $"    Hög avvisningsfrekvens kan indikera för strikta valideringsregler eller för liten ordlista"
            : $" Kunde inte generera korsord efter {maxAttempts} försök\n" +
              $"    Inga ord kunde placeras - kontrollera ordlista och generationsalternativ";
            
        throw new InvalidOperationException(message);
    }

    private async Task<CrosswordGrid?> TryGenerateSmartPuzzleAsync(CrosswordGrid grid, List<Word> candidateWords, List<WordAnalysis> sortedAnalysis, CrosswordGenerationOptions options, CancellationToken cancellationToken)
    {
        // Create a shuffled copy using top-biased Fisher-Yates (O(n) instead of O(n²) RemoveAt)
        var sortedWords = sortedAnalysis.ConvertAll(a => a.Word);
        ShuffleTopBiased(sortedWords, 5);

        // Phase 3: Smart anchor word selection with randomness
        // Track placed words with their scores for debugging
        var placedWordScores = new Dictionary<string, double>();
        
        if (!PlaceAnchorWordsWithValidation(grid, sortedWords, candidateWords, options))
        {
            return null;
        }

        // Phase 4: Main adaptive word placement
        var placedWords = grid.Words.ToHashSet();
        
        await PlaceWordsAdaptivelyWithValidation(grid, sortedWords, placedWords, options, placedWordScores, cancellationToken);

        // Derive gap filling parameters from grid dimensions
        var maxGapLength = Math.Max(grid.Width, grid.Height) - 2; // Allow gaps up to grid size minus margins
        var maxBridgeLength = Math.Min(maxGapLength, 10); // Cap bridges at 10 for performance

        // Phase 5: Multi-pass gap filling
        var gapFillingPasses = 10;
        for (int pass = 1; pass <= gapFillingPasses; pass++)
        {
            var beforeFill = grid.GetStats().FilledCells;
            await FillGapsAsync(grid, candidateWords, placedWords, options, placedWordScores, maxGapLength, cancellationToken);
            var afterFill = grid.GetStats().FilledCells;
            
            if (afterFill == beforeFill)
                break;
        }

        // Phase 6: Final short word pass
        await FillWithShortWordsAsync(grid, candidateWords, placedWords, options, placedWordScores, cancellationToken);

        // Phase 7: Bridge filling - find words that connect existing letters vertically/horizontally
        for (int pass = 1; pass <= 5; pass++)
        {
            var beforeFill = grid.GetStats().FilledCells;
            await FillBridgeOpportunitiesAsync(grid, candidateWords, placedWords, options, placedWordScores, maxBridgeLength, cancellationToken);
            var afterFill = grid.GetStats().FilledCells;
            
            if (afterFill == beforeFill)
                break;
        }

        // Phase 8: Another gap filling pass after bridges
        for (int pass = 1; pass <= 5; pass++)
        {
            var beforeFill = grid.GetStats().FilledCells;
            await FillGapsAsync(grid, candidateWords, placedWords, options, placedWordScores, maxGapLength, cancellationToken);
            await FillWithShortWordsAsync(grid, candidateWords, placedWords, options, placedWordScores, cancellationToken);
            var afterFill = grid.GetStats().FilledCells;
            
            if (afterFill == beforeFill)
                break;
        }

        // Phase 9: Validation
        var stats = grid.GetStats();
        var minWords = Math.Max(3, grid.Width / 4);
        
        if (placedWords.Count < minWords)
        {
            return null;
        }

        if (stats.FillPercentage < options.TargetFillPercentage)
        {
            return null;
        }

        if (!_validator.IsValidCrossword(grid))
        {
            return null;
        }

        var validation = grid.ValidateCrossword(_dictionary);
        
        if (options.RejectInvalidWords && validation.InvalidAccidentalWords.Any())
        {
            return null;
        }
        
        ReportGenerationResults(validation, grid.Words.Count, grid, placedWordScores);
        grid.FillEmptyCellsWithAsterisks();
        
        return grid;
    }

    #region Gap Detection and Filling

    /// <summary>
    /// Finds gaps (consecutive empty cells) in the grid that could fit words
    /// </summary>
    private List<GridGap> FindGaps(CrosswordGrid grid, int minLength = 2, int maxLength = 10)
    {
        var gaps = new List<GridGap>();
        
        // Find horizontal gaps (consecutive empty cells)
        for (int row = 0; row < grid.Height; row++)
        {
            int gapStart = -1;
            int gapLength = 0;
            
            for (int col = 0; col <= grid.Width; col++)
            {
                bool isEmpty = col < grid.Width && !grid.GetCell(row, col).HasLetter && !grid.GetCell(row, col).IsBlocked;
                
                if (isEmpty)
                {
                    if (gapStart == -1) gapStart = col;
                    gapLength++;
                }
                else
                {
                    if (gapLength >= minLength && gapLength <= maxLength)
                    {
                        bool leftBounded = gapStart == 0 || grid.GetCell(row, gapStart - 1).HasLetter || grid.GetCell(row, gapStart - 1).IsBlocked;
                        bool rightBounded = gapStart + gapLength >= grid.Width || grid.GetCell(row, gapStart + gapLength).HasLetter || grid.GetCell(row, gapStart + gapLength).IsBlocked;
                        
                        if (leftBounded || rightBounded)
                        {
                            gaps.Add(new GridGap
                            {
                                Row = row,
                                Col = gapStart,
                                Length = gapLength,
                                Direction = Direction.Across,
                                HasIntersections = CountIntersectionOpportunities(grid, row, gapStart, gapLength, Direction.Across)
                            });
                        }
                    }
                    gapStart = -1;
                    gapLength = 0;
                }
            }
        }
        
        // Find vertical gaps (consecutive empty cells)
        for (int col = 0; col < grid.Width; col++)
        {
            int gapStart = -1;
            int gapLength = 0;
            
            for (int row = 0; row <= grid.Height; row++)
            {
                bool isEmpty = row < grid.Height && !grid.GetCell(row, col).HasLetter && !grid.GetCell(row, col).IsBlocked;
                
                if (isEmpty)
                {
                    if (gapStart == -1) gapStart = row;
                    gapLength++;
                }
                else
                {
                    if (gapLength >= minLength && gapLength <= maxLength)
                    {
                        bool topBounded = gapStart == 0 || grid.GetCell(gapStart - 1, col).HasLetter || grid.GetCell(gapStart - 1, col).IsBlocked;
                        bool bottomBounded = gapStart + gapLength >= grid.Height || grid.GetCell(gapStart + gapLength, col).HasLetter || grid.GetCell(gapStart + gapLength, col).IsBlocked;
                        
                        if (topBounded || bottomBounded)
                        {
                            gaps.Add(new GridGap
                            {
                                Row = gapStart,
                                Col = col,
                                Length = gapLength,
                                Direction = Direction.Down,
                                HasIntersections = CountIntersectionOpportunities(grid, gapStart, col, gapLength, Direction.Down)
                            });
                        }
                    }
                    gapStart = -1;
                    gapLength = 0;
                }
            }
        }
        
        // Sort gaps by intersection opportunities (prefer gaps that connect to existing words)
        gaps.Sort((a, b) =>
        {
            int cmp = b.HasIntersections.CompareTo(a.HasIntersections);
            return cmp != 0 ? cmp : b.Length.CompareTo(a.Length);
        });
        return gaps;
    }

    private bool HasAdjacentLetterPerpendicular(CrosswordGrid grid, int row, int col, Direction wordDirection)
    {
        if (wordDirection == Direction.Across)
        {
            // Check above and below
            if (row > 0 && grid.GetCell(row - 1, col).HasLetter) return true;
            if (row < grid.Height - 1 && grid.GetCell(row + 1, col).HasLetter) return true;
        }
        else
        {
            // Check left and right
            if (col > 0 && grid.GetCell(row, col - 1).HasLetter) return true;
            if (col < grid.Width - 1 && grid.GetCell(row, col + 1).HasLetter) return true;
        }
        return false;
    }

    private int CountIntersectionOpportunities(CrosswordGrid grid, int startRow, int startCol, int length, Direction direction)
    {
        int count = 0;
        for (int i = 0; i < length; i++)
        {
            int row = direction == Direction.Across ? startRow : startRow + i;
            int col = direction == Direction.Across ? startCol + i : startCol;
            
            if (HasAdjacentLetterPerpendicular(grid, row, col, direction))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Attempts to fill gaps with matching words
    /// </summary>
    private async Task FillGapsAsync(CrosswordGrid grid, List<Word> candidateWords, HashSet<Word> placedWords, 
        CrosswordGenerationOptions options, Dictionary<string, double> placedWordScores, int maxGapLength, CancellationToken cancellationToken)
    {
        // minLength=2 because shortest Swedish words are 2 letters (e.g., "ÅL", "ÖL")
        var gaps = FindGaps(grid, minLength: 2, maxLength: maxGapLength);
        
        // Sort deterministically, then shuffle top candidates using Fisher-Yates
        gaps.Sort((a, b) =>
        {
            int cmp = (b.HasIntersections * 10).CompareTo(a.HasIntersections * 10);
            return cmp != 0 ? cmp : b.Length.CompareTo(a.Length);
        });
        ShuffleTopBiased(gaps, 3);
        
        // Cache placed word texts once for the entire gap-filling pass
        var usedWordTexts = grid.GetPlacedWordTexts();
        
        // Build a length-indexed lookup for candidate words (avoids scanning all candidates per gap)
        var candidatesByLength = new Dictionary<int, List<Word>>();
        var placedWordTexts = new HashSet<string>(placedWords.Select(w => w.Text), StringComparer.OrdinalIgnoreCase);
        foreach (var w in candidateWords)
        {
            if (placedWordTexts.Contains(w.Text) || usedWordTexts.Contains(w.Text))
                continue;
            if (!candidatesByLength.TryGetValue(w.Length, out var list))
            {
                list = new List<Word>();
                candidatesByLength[w.Length] = list;
            }
            list.Add(w);
        }
        
        int yieldCounter = 0;
        foreach (var gap in gaps)
        {
            if (cancellationToken.IsCancellationRequested) break;
            
            if (!candidatesByLength.TryGetValue(gap.Length, out var lengthCandidates))
                continue;
            
            // Score ALL candidates for this gap length, then take top 20
            var fittingWords = new List<(Word Word, double Score)>();
            foreach (var w in lengthCandidates)
            {
                if (usedWordTexts.Contains(w.Text)) continue;
                var score = ScoreWordForGap(w, grid, gap);
                fittingWords.Add((w, score));
            }
            
            if (fittingWords.Count == 0) continue;

            fittingWords.Sort((a, b) => b.Score.CompareTo(a.Score));
            if (fittingWords.Count > 20)
                fittingWords.RemoveRange(20, fittingWords.Count - 20);
            ShuffleTopBiased(fittingWords, 4);

            var tryCount = Math.Min(12, fittingWords.Count);
            for (int i = 0; i < tryCount; i++)
            {
                var (word, score) = fittingWords[i];
                if (grid.TryPlaceWordWithValidation(word, gap.Row, gap.Col, gap.Direction, _dictionary, options.RejectInvalidWords))
                {
                    placedWords.Add(word);
                    placedWordTexts.Add(word.Text);
                    usedWordTexts.Add(word.Text);
                    placedWordScores[word.Text] = score;
                    // Remove from length index so it's not tried again
                    lengthCandidates.Remove(word);
                    break;
                }
            }
            
            if (++yieldCounter % 10 == 0)
                await Task.Yield();
        }
    }

    private double ScoreWordForGap(Word word, CrosswordGrid grid, GridGap gap)
    {
        double score = 0;
        
        // Check how many letters would create valid intersections
        for (int i = 0; i < word.Length && i < gap.Length; i++)
        {
            int row = gap.Direction == Direction.Across ? gap.Row : gap.Row + i;
            int col = gap.Direction == Direction.Across ? gap.Col + i : gap.Col;
            
            // Bonus for positions that have adjacent letters (potential intersections)
            if (HasAdjacentLetterPerpendicular(grid, row, col, gap.Direction))
            {
                score += 2;
            }
        }
        
        // Bonus for common letters
        foreach (var c in word.Text)
        {
            if (c is 'A' or 'E' or 'I' or 'O' or 'U' or 'Å' or 'Ä' or 'Ö' or 'R' or 'N' or 'S' or 'T' or 'L')
                score += 0.3;
        }
        
        return score;
    }

    /// <summary>
    /// Final pass: try to fill remaining small gaps with 2-4 letter words
    /// </summary>
    private async Task FillWithShortWordsAsync(CrosswordGrid grid, List<Word> candidateWords, HashSet<Word> placedWords,
        CrosswordGenerationOptions options, Dictionary<string, double> placedWordScores, CancellationToken cancellationToken)
    {
        var usedWordTexts = grid.GetPlacedWordTexts();
        var placedWordTexts = new HashSet<string>(placedWords.Select(w => w.Text), StringComparer.OrdinalIgnoreCase);
        
        // Score and filter in single pass, pre-allocate capacity
        var shortWords = new List<(Word Word, double Score)>(100);
        foreach (var w in candidateWords)
        {
            if (w.Length < 2 || w.Length > 4) continue;
            if (placedWordTexts.Contains(w.Text)) continue;
            if (usedWordTexts.Contains(w.Text)) continue;
            
            var score = (double)CountVowels(w.Text);
            shortWords.Add((w, score));
        }
        
        // Sort by score descending
        shortWords.Sort((a, b) => b.Score.CompareTo(a.Score));
        
        // Limit and shuffle top-biased (O(n) instead of O(n²) RemoveAt)
        if (shortWords.Count > 80)
            shortWords.RemoveRange(80, shortWords.Count - 80);
        ShuffleTopBiased(shortWords, 5);
        
        // Find all possible intersections for short words
        int yieldCounter = 0;
        foreach (var (word, score) in shortWords)
        {
            if (cancellationToken.IsCancellationRequested) break;
            
            // Skip if this word text was placed in a previous iteration
            if (usedWordTexts.Contains(word.Text)) continue;
            
            var intersections = grid.GetPossibleIntersections(word).Take(15).ToList();
            if (intersections.Count == 0) continue;
            
            // Shuffle top-biased (O(n) instead of O(n²) RemoveAt)
            ShuffleTopBiased(intersections, 3);
            var tryCount = Math.Min(8, intersections.Count);
            
            for (int i = 0; i < tryCount; i++)
            {
                var (row, col, direction, _, _, _) = intersections[i];
                if (grid.TryPlaceWordWithValidation(word, row, col, direction, _dictionary, options.RejectInvalidWords))
                {
                    placedWords.Add(word);
                    placedWordTexts.Add(word.Text);
                    usedWordTexts.Add(word.Text);
                    placedWordScores[word.Text] = score;
                    break;
                }
            }
            
            if (++yieldCounter % 10 == 0)
                await Task.Yield();
        }
    }

    /// <summary>
    /// Finds vertical bridge opportunities - columns where existing letters from horizontal words
    /// could be connected by placing a vertical word that uses those letters.
    /// For example: if row 5 has 'K' at col 10 and row 7 has 'N' at col 10, 
    /// we could place "KAN" vertically starting at row 5, col 10.
    /// </summary>
    private List<VerticalBridgeOpportunity> FindVerticalBridgeOpportunities(CrosswordGrid grid, int minLength = 2, int maxLength = 8)
    {
        var opportunities = new List<VerticalBridgeOpportunity>();
        
        for (int col = 0; col < grid.Width; col++)
        {
            // Scan down the column to find sequences that include existing letters
            // We want to find: letter -> (empty or letter)* -> letter patterns
            
            int startRow = -1;
            var pattern = new List<(int Row, char? Letter)>();
            
            for (int row = 0; row <= grid.Height; row++)
            {
                bool isEnd = row >= grid.Height;
                bool hasLetter = !isEnd && grid.GetCell(row, col).HasLetter;
                bool isBlocked = !isEnd && grid.GetCell(row, col).IsBlocked;
                bool isEmpty = !isEnd && !hasLetter && !isBlocked;
                
                if (isBlocked || isEnd)
                {
                    // End of potential sequence - evaluate what we collected
                    if (pattern.Count >= minLength && pattern.Count <= maxLength)
                    {
                        // Check if this pattern has at least 2 existing letters with gaps between them
                        int letterCount = 0;
                        int emptyCount = 0;
                        foreach (var p in pattern)
                        {
                            if (p.Letter.HasValue) letterCount++;
                            else emptyCount++;
                        }
                        
                        if (letterCount >= 2 && emptyCount >= 1)
                        {
                            // This is a bridge opportunity!
                            opportunities.Add(new VerticalBridgeOpportunity
                            {
                                Col = col,
                                StartRow = startRow,
                                Length = pattern.Count,
                                Pattern = pattern.ConvertAll(p => p.Letter),
                                ExistingLetterCount = letterCount,
                                EmptyCellCount = emptyCount
                            });
                        }
                    }
                    
                    // Reset for next sequence
                    startRow = -1;
                    pattern.Clear();
                }
                else if (hasLetter || isEmpty)
                {
                    if (startRow == -1) startRow = row;
                    
                    char? letter = hasLetter ? grid.GetCell(row, col).Letter : null;
                    pattern.Add((row, letter));
                }
            }
        }
        
        // Also find horizontal bridge opportunities
        for (int row = 0; row < grid.Height; row++)
        {
            int startCol = -1;
            var pattern = new List<(int Col, char? Letter)>();
            
            for (int col = 0; col <= grid.Width; col++)
            {
                bool isEnd = col >= grid.Width;
                bool hasLetter = !isEnd && grid.GetCell(row, col).HasLetter;
                bool isBlocked = !isEnd && grid.GetCell(row, col).IsBlocked;
                bool isEmpty = !isEnd && !hasLetter && !isBlocked;
                
                if (isBlocked || isEnd)
                {
                    if (pattern.Count >= minLength && pattern.Count <= maxLength)
                    {
                        int letterCount = 0;
                        int emptyCount = 0;
                        foreach (var p in pattern)
                        {
                            if (p.Letter.HasValue) letterCount++;
                            else emptyCount++;
                        }
                        
                        if (letterCount >= 2 && emptyCount >= 1)
                        {
                            opportunities.Add(new VerticalBridgeOpportunity
                            {
                                Col = startCol,
                                StartRow = row,
                                Length = pattern.Count,
                                Pattern = pattern.ConvertAll(p => p.Letter),
                                ExistingLetterCount = letterCount,
                                EmptyCellCount = emptyCount,
                                IsHorizontal = true
                            });
                        }
                    }
                    
                    startCol = -1;
                    pattern.Clear();
                }
                else if (hasLetter || isEmpty)
                {
                    if (startCol == -1) startCol = col;
                    
                    char? letter = hasLetter ? grid.GetCell(row, col).Letter : null;
                    pattern.Add((col, letter));
                }
            }
        }
        
        // Sort by most existing letters (best bridge opportunities first)
        opportunities.Sort((a, b) =>
        {
            int cmp = b.ExistingLetterCount.CompareTo(a.ExistingLetterCount);
            return cmp != 0 ? cmp : a.EmptyCellCount.CompareTo(b.EmptyCellCount);
        });
        return opportunities;
    }

    /// <summary>
    /// Attempts to fill bridge opportunities with matching words
    /// </summary>
    private async Task FillBridgeOpportunitiesAsync(CrosswordGrid grid, List<Word> candidateWords, HashSet<Word> placedWords,
        CrosswordGenerationOptions options, Dictionary<string, double> placedWordScores, int maxBridgeLength, CancellationToken cancellationToken)
    {
        var usedWordTexts = grid.GetPlacedWordTexts();
        var placedWordTexts = new HashSet<string>(placedWords.Select(w => w.Text), StringComparer.OrdinalIgnoreCase);
        
        int yieldCounter = 0;
        int totalProcessed = 0;
        const int maxOpportunitiesPerPass = 50;
        
        // Re-discover opportunities after each successful placement so patterns stay fresh
        while (totalProcessed < maxOpportunitiesPerPass && !cancellationToken.IsCancellationRequested)
        {
            // minLength=2 because shortest Swedish words are 2 letters
            var opportunities = FindVerticalBridgeOpportunities(grid, minLength: 2, maxLength: maxBridgeLength);
            if (opportunities.Count == 0) break;
            
            bool placedAny = false;
            var limit = Math.Min(maxOpportunitiesPerPass - totalProcessed, opportunities.Count);
            
            for (int oppIdx = 0; oppIdx < limit; oppIdx++)
            {
                if (cancellationToken.IsCancellationRequested) break;
                totalProcessed++;
                
                var opportunity = opportunities[oppIdx];
                
                var matchingWords = FindWordsMatchingPattern(candidateWords, opportunity.Pattern, placedWordTexts, usedWordTexts);
                if (matchingWords.Count == 0) continue;
                
                // Score all matching words, then take top 10
                var scored = new List<(Word Word, double Score)>(matchingWords.Count);
                foreach (var w in matchingWords)
                {
                    double score = 0;
                    for (int i = 0; i < w.Length && i < opportunity.Pattern.Count; i++)
                    {
                        if (!opportunity.Pattern[i].HasValue)
                        {
                            var c = w.Text[i];
                            if (c is 'A' or 'E' or 'I' or 'O' or 'U' or 'Å' or 'Ä' or 'Ö')
                                score += 1.0;
                            else if (c is 'R' or 'N' or 'S' or 'T' or 'L')
                                score += 0.5;
                        }
                    }
                    scored.Add((w, score));
                }
                scored.Sort((a, b) => b.Score.CompareTo(a.Score));
                if (scored.Count > 10)
                    scored.RemoveRange(10, scored.Count - 10);
                ShuffleTopBiased(scored, 3);
                
                var tryCount = Math.Min(5, scored.Count);
                for (int i = 0; i < tryCount; i++)
                {
                    var word = scored[i].Word;
                    var direction = opportunity.IsHorizontal ? Direction.Across : Direction.Down;
                    var row = opportunity.StartRow;
                    var col = opportunity.Col;
                    
                    if (grid.TryPlaceWordWithValidation(word, row, col, direction, _dictionary, options.RejectInvalidWords))
                    {
                        placedWords.Add(word);
                        placedWordTexts.Add(word.Text);
                        usedWordTexts.Add(word.Text);
                        placedWordScores[word.Text] = opportunity.ExistingLetterCount * 10;
                        placedAny = true;
                        break;
                    }
                }
                
                // After a successful placement, break and re-discover with fresh grid state
                if (placedAny) break;
                
                if (++yieldCounter % 10 == 0)
                    await Task.Yield();
            }
            
            // If no word was placed in this discovery pass, stop trying
            if (!placedAny) break;
        }
    }

    private static int CountVowels(string text)
    {
        int count = 0;
        foreach (var c in text)
        {
            if (c is 'A' or 'E' or 'I' or 'O' or 'U' or 'Å' or 'Ä' or 'Ö')
                count++;
        }
        return count;
    }

    private async Task PlaceWordsAdaptivelyWithValidation(CrosswordGrid grid, List<Word> sortedWords, 
        HashSet<Word> placedWords, CrosswordGenerationOptions options, Dictionary<string, double> placedWordScores, CancellationToken cancellationToken)
    {
        const int maxConsecutiveFailures = 50;
        const int maxPlacementAttempts = 2000;
        
        var placementAttempts = 0;
        var currentTargetLength = options.MaxWordLength;
        var consecutiveFailures = 0;
        var triedWords = new HashSet<string>();
        bool requireIntersections = placedWords.Count > 0;
        
        // Pre-compute placed word texts for faster lookup
        var placedWordTexts = new HashSet<string>(placedWords.Select(w => w.Text), StringComparer.OrdinalIgnoreCase);

        // Cache used word texts and refresh periodically instead of every iteration
        var usedWordTexts = grid.GetPlacedWordTexts();
        int usedWordsRefreshCounter = 0;

        while (placementAttempts < maxPlacementAttempts && 
               currentTargetLength >= options.MinWordLength && 
               !cancellationToken.IsCancellationRequested)
        {
            // Refresh used word texts periodically (every 20 iterations or after a successful placement)
            if (usedWordsRefreshCounter >= 20)
            {
                usedWordTexts = grid.GetPlacedWordTexts();
                usedWordsRefreshCounter = 0;
            }
            
            // Use a window that always extends down to MinWordLength so short words aren't excluded
            var lengthMin = Math.Max(options.MinWordLength, currentTargetLength - 2);
            var availableWords = sortedWords
                .Where(w => !placedWordTexts.Contains(w.Text) 
                         && !usedWordTexts.Contains(w.Text)
                         && !triedWords.Contains(w.Text)
                         && w.Length >= lengthMin && w.Length <= currentTargetLength)
                .OrderBy(w => Math.Abs(w.Length - currentTargetLength))
                .ThenByDescending(w => CountVowels(w.Text))
                .Take(50)  // Limit early to avoid processing too many words
                .ToList();

            if (availableWords.Count == 0)
            {
                currentTargetLength--;
                consecutiveFailures = 0;
                triedWords.Clear();
                placementAttempts++; // Count this as an attempt so maxPlacementAttempts guard works
                continue;
            }

            placementAttempts++;
            usedWordsRefreshCounter++;

            // Direction-aware word selection with score tracking
            var (word, wordScore) = SelectBestWordWithDirectionBalanceAndScore(availableWords, grid, requireIntersections);
            if (word == null)
            {
                currentTargetLength--;
                consecutiveFailures = 0;
                triedWords.Clear();
                continue;
            }

            var placed = false;

            if (requireIntersections)
            {
                var preferredDirection = GetPreferredDirection(grid);
                
                var intersections = grid.GetPossibleIntersections(word)
                    .Select(i => new ScoredIntersection
                    {
                        Intersection = i,
                        Score = ScoreIntersectionWithDirectionBonus(i, grid, word.Length, preferredDirection)
                    })
                    .OrderByDescending(si => si.Score)
                    .Take(15)
                    .ToList();

                // Shuffle top-biased (O(n) instead of O(n²) RemoveAt)
                ShuffleTopBiased(intersections, 3);
                var tryCount = Math.Min(8, intersections.Count);

                for (int i = 0; i < tryCount; i++)
                {
                    var (row, col, direction, _, _, _) = intersections[i].Intersection;
                    if (grid.TryPlaceWordWithValidation(word, row, col, direction, _dictionary, options.RejectInvalidWords))
                    {
                        placedWords.Add(word);
                        placedWordTexts.Add(word.Text);
                        usedWordTexts.Add(word.Text);
                        placedWordScores[word.Text] = wordScore;
                        placed = true;
                        consecutiveFailures = 0;
                        usedWordsRefreshCounter = 20; // Force refresh next iteration
                        break;
                    }
                }
            }

            if (!placed && !requireIntersections)
            {
                var freePositions = FindOptimalFreePositions(grid, word).Take(5).ToList();
                foreach (var (row, col, direction) in freePositions)
                {
                    if (grid.TryPlaceWordWithValidation(word, row, col, direction, _dictionary, options.RejectInvalidWords))
                    {
                        placedWords.Add(word);
                        placedWordTexts.Add(word.Text);
                        usedWordTexts.Add(word.Text);
                        placedWordScores[word.Text] = wordScore;
                        placed = true;
                        consecutiveFailures = 0;
                        requireIntersections = true;
                        usedWordsRefreshCounter = 20; // Force refresh next iteration
                        break;
                    }
                }
            }

            if (!placed)
            {
                consecutiveFailures++;
                triedWords.Add(word.Text);
                
                if (consecutiveFailures >= maxConsecutiveFailures)
                {
                    currentTargetLength--;
                    consecutiveFailures = 0;
                    triedWords.Clear();
                }
            }
        }

        // Report results
        if (placedWords.Count > 0)
        {
            var finalStats = grid.GetStats();
            var avgWordLength = placedWords.Average(w => w.Length);
            Console.WriteLine($"Adaptiv placering: {finalStats.FillPercentage:F1}% fyllnad, {placedWords.Count} ord (snitt: {avgWordLength:F1})");
        }
    }
    
    private (Word? Word, double Score) SelectBestWordWithDirectionBalanceAndScore(List<Word> availableWords, CrosswordGrid grid, bool requireIntersections)
    {
        if (availableWords.Count == 0) return (null, 0);

        var preferredDirection = GetPreferredDirection(grid);
        
        // Score deterministically - limit to first 25 words
        var count = Math.Min(25, availableWords.Count);
        var scored = new List<(Word Word, int IntersectionCount, int PreferredCount, double Score)>(count);
        
        for (int i = 0; i < count; i++)
        {
            var word = availableWords[i];
            int intersectionCount = 0;
            int preferredDirectionIntersections = 0;
            
            if (requireIntersections)
            {
                foreach (var intersection in grid.GetPossibleIntersections(word))
                {
                    intersectionCount++;
                    if (intersection.Direction == preferredDirection)
                        preferredDirectionIntersections++;
                }
                
                // Skip words with no intersections when required
                if (intersectionCount == 0) continue;
            }
            else
            {
                intersectionCount = 1;
            }
            
            var score = CalculateAdaptiveWordScore(word, intersectionCount, requireIntersections) 
                      + preferredDirectionIntersections * 2;
            
            scored.Add((word, intersectionCount, preferredDirectionIntersections, score));
        }

        if (scored.Count == 0) return (null, 0);
        
        // Sort by score descending
        scored.Sort((a, b) => b.Score.CompareTo(a.Score));

        // Randomize pick index from top candidates
        var pickRange = Math.Min(4, scored.Count);
        var pickIndex = _random.Next(pickRange);
        var selected = scored[pickIndex];
        return (selected.Word, selected.Score);
    }

    private double CalculateAdaptiveWordScore(Word word, int intersectionCount, bool requireIntersections)
    {
        var score = 0.0;
        var length = word.Length;
        
        // Moderate length bonus - prefer medium-length words (5-10)
        if (length >= 5 && length <= 10)
            score += length * 1.5;
        else if (length < 5)
            score += length * 1.0;
        else
            score += 15.0; // Cap the length bonus at 10 letters (10 * 1.5)
        
        if (requireIntersections)
        {
            // Use passed intersection count instead of recomputing
            score += (intersectionCount / (double)length) * 10;
            
            // Count vowels and common consonants in single pass
            foreach (var c in word.Text)
            {
                if (c is 'A' or 'E' or 'I' or 'O' or 'U' or 'Å' or 'Ä' or 'Ö')
                    score += 0.5;
                else if (c is 'R' or 'N' or 'S' or 'T' or 'L')
                    score += 0.3;
            }
            
            // Normalize letter bonuses by length so long words don't accumulate
            // disproportionately high scores from having more letters
            if (length > 10)
                score -= (length - 10) * 0.3;
        }
        
        // Heavy penalty for rare long words (15+) - consistent with CalculateConnectivityScore
        if (length >= 15) score *= 0.05;  // 95% penalty for 15+ letter words
        if (length >= 16) score *= 0.05;  // Additional 95% penalty (total ~99.75%)

        return score;
    }

    private double ScoreIntersectionAdaptive((int Row, int Column, Direction Direction, Word IntersectingWord, int MyIndex, int TheirIndex) intersection, CrosswordGrid grid, int wordLength)
    {
        var (row, col, direction, intersectingWord, myIndex, theirIndex) = intersection;
        
        var score = 1.0;
        var sharedLetter = intersectingWord.GetCharAt(theirIndex);
        
        // Use pattern matching instead of string.Contains
        if (sharedLetter is 'A' or 'E' or 'I' or 'O' or 'U') score += 0.5;
        else if (sharedLetter is 'R' or 'N' or 'S' or 'T' or 'L') score += 0.3;
        
        var distanceFromEnd = Math.Min(myIndex, wordLength - myIndex - 1);
        score += distanceFromEnd * 0.2;
        
        var surroundingWords = CountNearbyWords(grid, row, col, 3);
        score -= surroundingWords * 0.15;
        
        if (intersectingWord.Length >= 6) score += 0.4;
        
        return score;
    }

    private Direction GetPreferredDirection(CrosswordGrid grid)
    {
        int acrossCount = 0;
        int downCount = 0;
        foreach (var w in grid.Words)
        {
            if (w.Direction == Direction.Across) acrossCount++;
            else downCount++;
        }
        
        // Return the direction we need more of
        return acrossCount <= downCount ? Direction.Across : Direction.Down;
    }

    private double ScoreIntersectionWithDirectionBonus(
        (int Row, int Column, Direction Direction, Word IntersectingWord, int MyIndex, int TheirIndex) intersection, 
        CrosswordGrid grid, int wordLength, Direction preferredDirection)
    {
        var score = ScoreIntersectionAdaptive(intersection, grid, wordLength);
        
        // Big bonus for preferred direction
        if (intersection.Direction == preferredDirection)
            score += 3;
        
        return score;
    }

    #endregion

    #region Word Analysis

    private string GetCacheDirectory()
    {
        // Allow overriding cache location via environment variable for CI/workflows
        var env = Environment.GetEnvironmentVariable("SWEDISH_CROSSWORD_CACHE_PATH");
        if (!string.IsNullOrWhiteSpace(env))
        {
            try
            {
                // Expand leading ~ to user profile if present
                if (env.StartsWith("~"))
                {
                    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    env = Path.Combine(home, env.TrimStart('~').TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                }

                return Path.GetFullPath(env);
            }
            catch
            {
                // Fall back to default if expansion fails
            }
        }

        // Default to LocalApplicationData/SwedishCrossword
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwedishCrossword");
    }

    private string GetCacheFilePath()
    {
        var dir = GetCacheDirectory();
        return Path.Combine(dir, CacheFileName);
    }

    private List<WordAnalysis>? LoadAnalysisFromDisk(string fingerprint, List<Word> words)
    {
        try
        {
            var filePath = GetCacheFilePath();
            if (!File.Exists(filePath)) return null;

            var json = File.ReadAllText(filePath);
            var payload = JsonSerializer.Deserialize<CacheFilePayload>(json);
            if (payload == null) return null;
            if (payload.Fingerprint != fingerprint) return null;

            // Validate entry count matches word list size
            if (payload.Entries.Count != words.Count) return null;

            // Map DTOs back to WordAnalysis using the provided words list
            var wordMap = words.ToDictionary(w => w.Text, StringComparer.OrdinalIgnoreCase);
            var result = new List<WordAnalysis>(payload.Entries.Count);
            foreach (var dto in payload.Entries)
            {
                if (!wordMap.TryGetValue(dto.Text, out var word))
                {
                    // A word from cache is missing from current word list - cache invalid
                    return null;
                }

                result.Add(new WordAnalysis
                {
                    Word = word,
                    ConnectivityScore = dto.ConnectivityScore,
                    VowelCount = dto.VowelCount,
                    CommonLetterCount = dto.CommonLetterCount
                });
            }

            return result;
        }
        catch
        {
            // If any IO/deserialization error occurs, ignore and let caller recompute
            return null;
        }
    }

    private void SaveAnalysisToDisk(string fingerprint, List<WordAnalysis> analysis)
    {
        try
        {
            var dir = GetCacheDirectory();
            Directory.CreateDirectory(dir);
            var filePath = Path.Combine(dir, CacheFileName);

            var dtos = analysis.ConvertAll(a => new WordAnalysisDto(a.Word.Text, a.ConnectivityScore, a.VowelCount, a.CommonLetterCount));
            var payload = new CacheFilePayload(fingerprint, dtos);
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
        catch
        {
            // Ignore disk errors - caching should be best-effort
        }
    }

    private List<WordAnalysis> AnalyzeWordConnectivity(List<Word> words)
    {
        ArgumentNullException.ThrowIfNull(words);

        // Compute fingerprint first (cheap relative to full analysis)
        var fingerprint = ComputeWordsFingerprint(words);

        lock (_analysisCacheLock)
        {
            if (fingerprint == _cachedWordsFingerprint && _cachedWordAnalysis != null)
            {
                // Return a copy to avoid accidental external mutation
                return new List<WordAnalysis>(_cachedWordAnalysis);
            }

            // Try load from disk cache before computing
            var disk = LoadAnalysisFromDisk(fingerprint, words);
            if (disk != null)
            {
                _cachedWordsFingerprint = fingerprint;
                _cachedWordAnalysis = new List<WordAnalysis>(disk);
                return new List<WordAnalysis>(_cachedWordAnalysis);
            }

            // Precompute letter-to-words index: for each letter, how many words contain it
            // This replaces the O(n) string.Contains scan per letter per word pair
            var letterWordCount = new Dictionary<char, int>();
            foreach (var word in words)
            {
                var seen = new HashSet<char>();
                foreach (var c in word.Text)
                {
                    if (seen.Add(c))
                    {
                        letterWordCount[c] = letterWordCount.GetValueOrDefault(c, 0) + 1;
                    }
                }
            }

            var analysis = new List<WordAnalysis>(words.Count);
            foreach (var word in words)
            {
                var (connectivityScore, vowelCount, commonLetterCount) = CalculateConnectivityScore(word, letterWordCount);
                analysis.Add(new WordAnalysis
                {
                    Word = word,
                    ConnectivityScore = connectivityScore,
                    VowelCount = vowelCount,
                    CommonLetterCount = commonLetterCount
                });
            }

            // Update cache with a copy
            _cachedWordsFingerprint = fingerprint;
            _cachedWordAnalysis = new List<WordAnalysis>(analysis);

            // Persist to disk (best-effort)
            SaveAnalysisToDisk(fingerprint, _cachedWordAnalysis);

            return analysis;
        }
    }

    private static string ComputeWordsFingerprint(List<Word> words)
    {
        // Hash incrementally instead of building a massive concatenated string
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        
        // Sort word texts for deterministic fingerprint
        var sortedTexts = words.ConvertAll(w => w.Text);
        sortedTexts.Sort(StringComparer.Ordinal);
        
        foreach (var text in sortedTexts)
        {
            // Prefix each word with its length to prevent ambiguous concatenation
            // e.g., ["AB","CD"] -> "2:AB2:CD" vs ["ABC","D"] -> "3:ABC1:D"
            var lengthPrefix = Encoding.UTF8.GetBytes($"{text.Length}:");
            sha256.AppendData(lengthPrefix);
            sha256.AppendData(Encoding.UTF8.GetBytes(text));
        }
        
        var hashBytes = sha256.GetHashAndReset();
        return Convert.ToHexStringLower(hashBytes);
    }

    private static (double Score, int VowelCount, int CommonLetterCount) CalculateConnectivityScore(
        Word targetWord, Dictionary<char, int> letterWordCount)
    {
        var score = 0.0;
        var letterFreq = new Dictionary<char, int>();
        int vowelCount = 0;
        int commonLetterCount = 0;
        
        // Single pass through the word to build frequency map and count letter types
        foreach (var c in targetWord.Text)
        {
            letterFreq[c] = letterFreq.GetValueOrDefault(c, 0) + 1;
            
            // Count vowels and common letters in same pass
            if (c is 'A' or 'E' or 'I' or 'O' or 'U' or 'Å' or 'Ä' or 'Ö')
            {
                vowelCount++;
                commonLetterCount++;
                
                if (c is 'A' or 'E' or 'I' or 'O' or 'U')
                    score += 0.3;
                else
                    score += 0.2;
            }
            else if (c is 'R' or 'N' or 'S' or 'T' or 'L' or 'K')
            {
                commonLetterCount++;
                if (c is 'R' or 'N' or 'S' or 'T' or 'L')
                    score += 0.5;
            }
        }

        // Use precomputed letter-to-word-count index instead of O(n) scan per letter
        foreach (var kvp in letterFreq)
        {
            if (letterWordCount.TryGetValue(kvp.Key, out var wordCount))
            {
                // Subtract 1 because letterWordCount includes targetWord itself
                var otherWordCount = wordCount - 1;
                if (otherWordCount > 0)
                {
                    score += otherWordCount * (kvp.Value / Math.Sqrt(kvp.Value));
                }
            }
        }

        // Normalize by word length so long words don't accumulate disproportionately high raw scores
        // before the length penalty is applied. Without this, a 16-letter word with 10 common letters
        // can have a raw score of 4000+, and even a 99.75% penalty leaves it at ~10 (competitive with
        // shorter words). Dividing by length brings scores into a comparable range across lengths.
        if (targetWord.Length > 0)
            score /= targetWord.Length;

        if (targetWord.Length >= 15) score *= 0.05;
        if (targetWord.Length >= 16) score *= 0.05;
        
        return (score, vowelCount, commonLetterCount);
    }

    #endregion

    #region Anchor Word Selection

    private bool PlaceAnchorWordsWithValidation(CrosswordGrid grid, List<Word> sortedWords, List<Word> allWords, 
        CrosswordGenerationOptions options)
    {
        var placed = 0;
        var usedWordTexts = grid.GetPlacedWordTexts();
        
        var maxAnchorLength = Math.Min(14, options.Width);
        var minAnchorLength = Math.Max(1, maxAnchorLength - 3);

        // Precompute letter-to-words index: for each letter, how many words contain it
        // This replaces the O(n) string.Contains scan per letter per word pair
        var letterWordCount = new Dictionary<char, int>();
        foreach (var word in allWords)
        {
            var seen = new HashSet<char>();
            foreach (var c in word.Text)
            {
                if (seen.Add(c))
                {
                    letterWordCount[c] = letterWordCount.GetValueOrDefault(c, 0) + 1;
                }
            }
        }
        
        var filteredCandidates = new List<Word>(100);
        foreach (var w in allWords)
        {
            if (w.Length >= minAnchorLength && w.Length <= maxAnchorLength && !usedWordTexts.Contains(w.Text))
                filteredCandidates.Add(w);
        }

        var anchorCandidates = new List<(Word Word, double Score)>(filteredCandidates.Count);
        foreach (var w in filteredCandidates)
        {
            var score = ScoreAnchorWordWithIntersectionPotential(w, letterWordCount);
            anchorCandidates.Add((w, score));
        }
        
        anchorCandidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        
        Word? bestAnchor = null;
        if (anchorCandidates.Count > 0)
        {
            var pickIndex = _random.Next(Math.Min(5, Math.Min(8, anchorCandidates.Count)));
            bestAnchor = anchorCandidates[pickIndex].Word;
        }
        else
        {
            bestAnchor = sortedWords.FirstOrDefault(w => !usedWordTexts.Contains(w.Text));
        }

        if (bestAnchor == null)
            return false;

        var centerRow = options.Height / 2 + _random.Next(-1, 2);
        var centerCol = Math.Max(0, (options.Width - bestAnchor.Length) / 2 + _random.Next(-1, 2));
        
        if (grid.TryPlaceWordWithValidation(bestAnchor, centerRow, centerCol, Direction.Across, _dictionary, options.RejectInvalidWords))
        {
            placed++;
            usedWordTexts = grid.GetPlacedWordTexts();
        }

        if (placed > 0 && sortedWords.Count > 1)
        {
            var anchorLetters = new HashSet<char>(bestAnchor.Text);
            
            var filteredSecondCandidates = new List<Word>(100);
            foreach (var w in allWords)
            {
                if (w.Length < minAnchorLength || w.Length > maxAnchorLength) continue;
                if (w == bestAnchor || usedWordTexts.Contains(w.Text)) continue;
                
                bool hasSharedLetter = false;
                foreach (var c in w.Text)
                {
                    if (anchorLetters.Contains(c))
                    {
                        hasSharedLetter = true;
                        break;
                    }
                }
                if (hasSharedLetter)
                    filteredSecondCandidates.Add(w);
            }

            var candidateSecondWords = new List<(Word Word, double Score)>(filteredSecondCandidates.Count);
            foreach (var w in filteredSecondCandidates)
            {
                var score = ScoreSecondAnchorWithIntersectionPotential(w, bestAnchor, letterWordCount);
                candidateSecondWords.Add((w, score));
            }
            
            candidateSecondWords.Sort((a, b) => b.Score.CompareTo(a.Score));
            var topCount = Math.Min(20, candidateSecondWords.Count);

            var shuffledCandidates = new List<Word>(topCount);
            for (int i = 0; i < topCount; i++)
            {
                shuffledCandidates.Add(candidateSecondWords[i].Word);
            }
            
            ShuffleTopBiased(shuffledCandidates, 4);
            
            foreach (var secondWord in shuffledCandidates)
            {
                var intersections = grid.GetPossibleIntersections(secondWord)
                    .Select(i => (Intersection: i, Score: ScoreAnchorIntersection(i, grid)))
                    .OrderByDescending(x => x.Score)
                    .Take(8)
                    .ToList();
                
                ShuffleTopBiased(intersections, 3);
                var tryCount = Math.Min(5, intersections.Count);
                
                for (int i = 0; i < tryCount; i++)
                {
                    var (row, col, direction, _, _, _) = intersections[i].Intersection;
                    if (grid.TryPlaceWordWithValidation(secondWord, row, col, direction, _dictionary, options.RejectInvalidWords))
                    {
                        placed++;
                        break;
                    }
                }
                
                if (placed > 1) break;
            }
        }

        return placed > 0;
    }

    private double ScoreAnchorWordWithIntersectionPotential(Word word, Dictionary<char, int> letterWordCount)
    {
        double score = 0;
        
        var uniqueLetters = new HashSet<char>();
        foreach (var c in word.Text)
        {
            uniqueLetters.Add(c);
            
            if (c is 'A' or 'E' or 'I' or 'O' or 'U')
                score += 1.5;
            else if (c is 'R' or 'N' or 'S' or 'T' or 'L')
                score += 1.0;
            else if (c is 'Å' or 'Ä' or 'Ö')
                score += 0.5;
        }
        
        // Both conditions can now trigger independently for overlapping lengths (6-8)
        if (word.Length >= 6 && word.Length <= 9)
            score += 3;
        if (word.Length >= 5 && word.Length <= 8)
            score += 2;
        
        score += uniqueLetters.Count * 0.5;
        
        // Use precomputed letter-to-word-count instead of O(n*k) scan
        int intersectionPotential = 0;
        foreach (var letter in uniqueLetters)
        {
            if (letterWordCount.TryGetValue(letter, out var count))
            {
                // Subtract 1 to exclude this word itself
                intersectionPotential += count - 1;
            }
        }
        score += intersectionPotential / 500.0;
        
        if (word.Length >= 15) score *= 0.05;
        if (word.Length >= 16) score *= 0.05;
        
        return score;
    }

    private double ScoreSecondAnchorWithIntersectionPotential(Word word, Word firstAnchor, Dictionary<char, int> letterWordCount)
    {
        double score = ScoreAnchorWordWithIntersectionPotential(word, letterWordCount);
        
        var sharedLetters = word.Text.Intersect(firstAnchor.Text).Count();
        score += sharedLetters * 3;
        
        var newLetters = word.Text.Except(firstAnchor.Text).Distinct().Count();
        score += newLetters * 1.5;
        
        return score;
    }

    private double ScoreAnchorIntersection((int Row, int Column, Direction Direction, Word IntersectingWord, int MyIndex, int TheirIndex) intersection, CrosswordGrid grid)
    {
        var (row, col, direction, _, myIndex, _) = intersection;
        
        double score = 1.0;
        
        // Use half the word length as the reference so the bonus scales with word length
        var halfLength = intersection.IntersectingWord.Length / 2.0;
        var distanceFromMiddle = Math.Abs(myIndex - halfLength);
        score += Math.Max(0, halfLength - distanceFromMiddle) * 0.5;
        
        var centerDistance = Math.Abs(row - grid.Height / 2.0) + Math.Abs(col - grid.Width / 2.0);
        score -= centerDistance * 0.1;
        
        return score;
    }

    #endregion

    #region Pattern Matching

    /// <summary>
    /// Finds words that match a pattern with some letters fixed and some as wildcards
    /// </summary>
    private static List<Word> FindWordsMatchingPattern(List<Word> candidateWords, List<char?> pattern, 
        HashSet<string> placedWordTexts, HashSet<string> usedWordTexts)
    {
        var matches = new List<Word>();
        var patternLength = pattern.Count;
        
        foreach (var word in candidateWords)
        {
            if (word.Length != patternLength) continue;
            if (placedWordTexts.Contains(word.Text)) continue;
            if (usedWordTexts.Contains(word.Text)) continue;
            
            bool isMatch = true;
            for (int i = 0; i < patternLength; i++)
            {
                if (pattern[i].HasValue && word.Text[i] != pattern[i].Value)
                {
                    isMatch = false;
                    break;
                }
            }
            
            if (isMatch)
            {
                matches.Add(word);
            }
        }
        
        return matches;
    }

    #endregion

    #region Helper Methods

    private int CountNearbyWords(CrosswordGrid grid, int centerRow, int centerCol, int radius)
    {
        var count = 0;
        var minRow = Math.Max(0, centerRow - radius);
        var maxRow = Math.Min(grid.Height - 1, centerRow + radius);
        var minCol = Math.Max(0, centerCol - radius);
        var maxCol = Math.Min(grid.Width - 1, centerCol + radius);
        
        for (int r = minRow; r <= maxRow; r++)
        {
            for (int c = minCol; c <= maxCol; c++)
            {
                if (r == centerRow && c == centerCol) continue;
                if (grid.GetCell(r, c).HasLetter) count++;
            }
        }
        return count;
    }

    private IEnumerable<(int Row, int Column, Direction Direction)> FindOptimalFreePositions(CrosswordGrid grid, Word word)
    {
        var positions = new List<(int Row, int Column, Direction Direction, double Score)>();
        var directions = _random.NextDouble() < 0.5 
            ? new[] { Direction.Across, Direction.Down }
            : new[] { Direction.Down, Direction.Across };

        foreach (var dir in directions)
        {
            var maxRow = dir == Direction.Across ? grid.Height : grid.Height - word.Length + 1;
            var maxCol = dir == Direction.Across ? grid.Width - word.Length + 1 : grid.Width;

            for (int row = 0; row < maxRow; row++)
            {
                for (int col = 0; col < maxCol; col++)
                {
                    if (grid.CanPlaceWord(word, row, col, dir))
                    {
                        var score = ScoreFreePosition(grid, row, col, dir, word);
                        positions.Add((row, col, dir, score));
                    }
                }
            }
        }

        return positions.OrderByDescending(p => p.Score).Select(p => (p.Row, p.Column, p.Direction));
    }

    private double ScoreFreePosition(CrosswordGrid grid, int row, int col, Direction direction, Word word)
    {
        var score = 0.0;
        
        var centerDistance = Math.Sqrt(Math.Pow(row - grid.Height / 2.0, 2) + Math.Pow(col - grid.Width / 2.0, 2));
        score -= centerDistance * 0.1;
        
        var preferredDirection = GetPreferredDirection(grid);
        if (direction == preferredDirection)
            score += 1.0;
        
        return score;
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

        // Detailed placed word scores for debugging
        Console.WriteLine("Detaljerad poängsättning av placerade ord:");
        foreach (var kvp in placedWordScores.OrderByDescending(kvp => kvp.Value))
        {
            Console.WriteLine($"  {kvp.Key}: {kvp.Value:F2}");
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

    #endregion

    #region Helper Classes

    private class WordAnalysis
    {
        public Word Word { get; set; } = null!;
        public double ConnectivityScore { get; set; }
        public int VowelCount { get; set; }
        public int CommonLetterCount { get; set; }
    }

    private class ScoredIntersection
    {
        public (int Row, int Column, Direction Direction, Word IntersectingWord, int MyIndex, int TheirIndex) Intersection { get; set; }
        public double Score { get; set; }
    }

    private class GridGap
    {
        public int Row { get; set; }
        public int Col { get; set; }
        public int Length { get; set; }
        public Direction Direction { get; set; }
        public int HasIntersections { get; set; }
    }

    private class VerticalBridgeOpportunity
    {
        public int Col { get; set; }
        public int StartRow { get; set; }
        public int Length { get; set; }
        public List<char?> Pattern { get; set; } = new();
        public int ExistingLetterCount { get; set; }
        public int EmptyCellCount { get; set; }
        public bool IsHorizontal { get; set; } = false;
    }

    #endregion
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
        TargetFillPercentage = 65.0,
        Difficulty = null,
        MaxAttempts = 120,
        RejectInvalidWords = true
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