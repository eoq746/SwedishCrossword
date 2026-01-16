using SwedishCrossword.Models;

namespace SwedishCrossword.Services;

/// <summary>
/// Main service for generating Swedish crossword puzzles with advanced placement strategies
/// </summary>
public class CrosswordGenerator
{
    private readonly SwedishDictionary _dictionary;
    private readonly GridValidator _validator;
    private readonly Random _random;

    public CrosswordGenerator(SwedishDictionary dictionary, GridValidator validator)
    {
        _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _random = new Random();
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

        while (attempts < maxAttempts && !cancellationToken.IsCancellationRequested)
        {
            attempts++;
            
            try
            {
                var grid = new CrosswordGrid(options.Width, options.Height);
                var result = await TryGenerateSmartPuzzleAsync(grid, options, cancellationToken);
                
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

            await Task.Delay(5, cancellationToken);
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

    private async Task<CrosswordGrid?> TryGenerateSmartPuzzleAsync(CrosswordGrid grid, CrosswordGenerationOptions options, CancellationToken cancellationToken)
    {
        // Phase 1: Get and analyze candidate words
        var candidateWords = GetCandidateWords(options).ToList();
        if (candidateWords.Count == 0)
        {
            throw new InvalidOperationException("No suitable words found for the specified criteria");
        }

        // Phase 2: Analyze word connectivity and sort strategically with randomness
        var wordAnalysis = AnalyzeWordConnectivity(candidateWords);
        var sortedWords = wordAnalysis
            .OrderByDescending(w => w.ConnectivityScore + _random.NextDouble() * 5) // Add randomness to ordering
            .ThenBy(w => w.Word.Length + _random.Next(-1, 2)) // Slight length variation
            .Select(w => w.Word)
            .ToList();

        // Phase 3: Smart anchor word selection with randomness
        // Track used word texts to prevent duplicates - use case-insensitive comparison for robustness
        var usedWordTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        if (!PlaceAnchorWordsWithValidation(grid, sortedWords, candidateWords, options, usedWordTexts))
        {
            return null;
        }

        // Phase 4: Main adaptive word placement
        var placedWords = grid.Words.ToHashSet();
        // Sync usedWordTexts with already placed words
        foreach (var word in placedWords)
        {
            usedWordTexts.Add(word.Text);
        }
        
        await PlaceWordsAdaptivelyWithValidation(grid, sortedWords, placedWords, usedWordTexts, options, cancellationToken);

        // Phase 5: Multi-pass gap filling - try to fill remaining gaps
        var gapFillingPasses = 3;
        for (int pass = 1; pass <= gapFillingPasses; pass++)
        {
            var beforeFill = grid.GetStats().FilledCells;
            await FillGapsAsync(grid, candidateWords, placedWords, usedWordTexts, options, cancellationToken);
            var afterFill = grid.GetStats().FilledCells;
            
            if (afterFill == beforeFill)
                break; // No progress, stop trying
        }

        // Phase 6: Final short word pass - try 2-3 letter words in remaining gaps
        await FillWithShortWordsAsync(grid, candidateWords, placedWords, usedWordTexts, options, cancellationToken);

        // Phase 7: Validation
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
        
        ReportGenerationResults(validation, usedWordTexts.Count);
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
        
        // Find horizontal gaps
        for (int row = 0; row < grid.Height; row++)
        {
            int gapStart = -1;
            int gapLength = 0;
            
            for (int col = 0; col <= grid.Width; col++)
            {
                bool isEmpty = col < grid.Width && !grid.GetCell(row, col).HasLetter && !grid.GetCell(row, col).IsBlocked;
                bool hasIntersection = col < grid.Width && HasAdjacentLetterPerpendicular(grid, row, col, Direction.Across);
                
                if (isEmpty)
                {
                    if (gapStart == -1) gapStart = col;
                    gapLength++;
                }
                else
                {
                    if (gapLength >= minLength && gapLength <= maxLength)
                    {
                        // Check if gap is bounded properly (not adjacent to letters in same direction)
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
        
        // Find vertical gaps
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
        return gaps.OrderByDescending(g => g.HasIntersections)
                   .ThenByDescending(g => g.Length)
                   .ToList();
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
        HashSet<string> usedWordTexts, CrosswordGenerationOptions options, CancellationToken cancellationToken)
    {
        var gaps = FindGaps(grid, 2, 8);
        
        // Shuffle gaps slightly for variety
        gaps = gaps.OrderBy(g => g.HasIntersections * 10 + _random.Next(5))
                   .ThenByDescending(g => g.Length)
                   .Reverse()
                   .ToList();
        
        foreach (var gap in gaps)
        {
            if (cancellationToken.IsCancellationRequested) break;
            
            // Find words that could fit this gap with randomness, excluding already used words
            var fittingWords = candidateWords
                .Except(placedWords)
                .Where(w => !usedWordTexts.Contains(w.Text)) // Exclude words with same text
                .Where(w => w.Length == gap.Length)
                .Select(w => new { Word = w, Score = ScoreWordForGap(w, grid, gap) + _random.NextDouble() * 2 })
                .OrderByDescending(w => w.Score)
                .Take(15) // More candidates
                .Select(w => w.Word)
                .ToList();

            // Pick from top candidates with some randomness
            var wordsToTry = fittingWords.Take(5).OrderBy(_ => _random.Next()).Concat(fittingWords.Skip(5)).Take(10);
            
            foreach (var word in wordsToTry)
            {
                if (grid.TryPlaceWordWithValidation(word, gap.Row, gap.Col, gap.Direction, _dictionary, options.RejectInvalidWords))
                {
                    placedWords.Add(word);
                    usedWordTexts.Add(word.Text); // Track the word text
                    break;
                }
            }
            
            await Task.Yield(); // Allow cancellation
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
        score += word.Text.Count(c => "AEIOUÅÄÖRNSTL".Contains(c)) * 0.3;
        
        return score;
    }

    /// <summary>
    /// Final pass: try to fill remaining small gaps with 2-3 letter words
    /// </summary>
    private async Task FillWithShortWordsAsync(CrosswordGrid grid, List<Word> candidateWords, HashSet<Word> placedWords,
        HashSet<string> usedWordTexts, CrosswordGenerationOptions options, CancellationToken cancellationToken)
    {
        var shortWords = candidateWords
            .Except(placedWords)
            .Where(w => !usedWordTexts.Contains(w.Text)) // Exclude words with same text
            .Where(w => w.Length >= 2 && w.Length <= 4)
            .Select(w => new { Word = w, Score = w.Text.Count(c => "AEIOU???".Contains(c)) + _random.NextDouble() * 2 })
            .OrderByDescending(w => w.Score)
            .Select(w => w.Word)
            .ToList();
        
        // Shuffle the order slightly for variety
        var shuffledWords = shortWords.Take(20).OrderBy(_ => _random.Next())
                                      .Concat(shortWords.Skip(20))
                                      .Take(60)
                                      .ToList();
        
        // Find all possible intersections for short words
        foreach (var word in shuffledWords)
        {
            if (cancellationToken.IsCancellationRequested) break;
            
            // Skip if this word text was used in a previous iteration
            if (usedWordTexts.Contains(word.Text)) continue;
            
            var intersections = grid.GetPossibleIntersections(word)
                .OrderBy(_ => _random.Next()) // Randomize intersection order
                .Take(5)
                .ToList();
            
            foreach (var (row, col, direction, _, _, _) in intersections)
            {
                if (grid.TryPlaceWordWithValidation(word, row, col, direction, _dictionary, options.RejectInvalidWords))
                {
                    placedWords.Add(word);
                    usedWordTexts.Add(word.Text); // Track the word text
                    break;
                }
            }
            
            await Task.Yield();
        }
    }

    #endregion

    #region Improved Anchor Selection

    private bool PlaceAnchorWordsWithValidation(CrosswordGrid grid, List<Word> sortedWords, List<Word> allWords, 
        CrosswordGenerationOptions options, HashSet<string> usedWordTexts)
    {
        var placed = 0;

        // Select first anchor word: combine letter scoring with actual intersection potential + randomness
        var anchorCandidates = allWords
            .Where(w => w.Length >= 5 && w.Length <= Math.Min(10, options.Width - 2))
            .Where(w => !usedWordTexts.Contains(w.Text)) // Exclude already used words
            .Select(w => new
            {
                Word = w,
                Score = ScoreAnchorWordWithIntersectionPotential(w, allWords) + _random.NextDouble() * 8 // Add randomness
            })
            .OrderByDescending(w => w.Score)
            .Take(5) // Take top 5 candidates
            .ToList();

        // Randomly select from top candidates (weighted towards higher scores)
        var bestAnchor = anchorCandidates.Count > 0
            ? anchorCandidates[_random.Next(Math.Min(3, anchorCandidates.Count))].Word
            : sortedWords.FirstOrDefault(w => !usedWordTexts.Contains(w.Text));

        if (bestAnchor == null)
            return false;

        var centerRow = options.Height / 2;
        var centerCol = Math.Max(0, (options.Width - bestAnchor.Length) / 2);
        
        if (grid.TryPlaceWordWithValidation(bestAnchor, centerRow, centerCol, Direction.Across, _dictionary, options.RejectInvalidWords))
        {
            placed++;
            usedWordTexts.Add(bestAnchor.Text);
        }

        // Second anchor - find word with best intersection potential with first, with randomness
        if (placed > 0 && sortedWords.Count > 1)
        {
            var candidateSecondWords = allWords
                .Where(w => w != bestAnchor && !usedWordTexts.Contains(w.Text)) // Exclude already used words
                .Where(w => w.Text.Any(c => bestAnchor.Text.Contains(c)))
                .Select(w => new
                {
                    Word = w,
                    Score = ScoreSecondAnchorWithIntersectionPotential(w, bestAnchor, allWords) + _random.NextDouble() * 5
                })
                .OrderByDescending(w => w.Score)
                .Select(w => w.Word)
                .Take(15) // More candidates for variety
                .ToList();

            // Shuffle the top candidates slightly
            if (candidateSecondWords.Count > 3)
            {
                var topPart = candidateSecondWords.Take(5).OrderBy(_ => _random.Next()).ToList();
                candidateSecondWords = topPart.Concat(candidateSecondWords.Skip(5)).ToList();
            }
            
            foreach (var secondWord in candidateSecondWords)
            {
                var intersections = grid.GetPossibleIntersections(secondWord)
                    .OrderByDescending(i => ScoreAnchorIntersection(i, grid) + _random.NextDouble() * 0.5)
                    .Take(5)
                    .ToList();
                
                foreach (var (row, col, direction, _, _, _) in intersections)
                {
                    if (grid.TryPlaceWordWithValidation(secondWord, row, col, direction, _dictionary, options.RejectInvalidWords))
                    {
                        placed++;
                        usedWordTexts.Add(secondWord.Text);
                        break;
                    }
                }
                
                if (placed > 1) break;
            }
        }

        return placed > 0;
    }

    /// <summary>
    /// Scores anchor word using hybrid approach: letter quality + actual intersection potential
    /// </summary>
    private double ScoreAnchorWordWithIntersectionPotential(Word word, List<Word> allWords)
    {
        double score = 0;
        
        // Part 1: Letter-based scoring (original approach, reduced weight)
        score += word.Text.Count(c => "AEIOU".Contains(c)) * 1.5;  // Vowels (reduced from 3)
        score += word.Text.Count(c => "RNSTL".Contains(c)) * 1.0;  // Common consonants (reduced from 2)
        score += word.Text.Count(c => "ÅÄÖ".Contains(c)) * 0.5;    // Swedish-specific (reduced from 1)
        
        // Length bonus (prefer 6-9 for better intersection potential)
        if (word.Length >= 6 && word.Length <= 9)
            score += 3;
        else if (word.Length >= 5 && word.Length <= 8)
            score += 2;
        
        // Unique letters bonus
        score += word.Text.Distinct().Count() * 0.5;
        
        // Part 2: Actual intersection potential (NEW - major factor)
        var intersectionPotential = CalculateIntersectionPotential(word, allWords);
        score += intersectionPotential / 500.0;  // Normalize to reasonable range (typical values 15000-21000)
        
        return score;
    }

    /// <summary>
    /// Calculates how many other words can intersect with this word
    /// </summary>
    private int CalculateIntersectionPotential(Word word, List<Word> allWords)
    {
        int total = 0;
        foreach (var letter in word.Text.Distinct())
        {
            total += allWords.Count(other => other.Text != word.Text && other.Text.Contains(letter));
        }
        return total;
    }

    private double ScoreSecondAnchorWithIntersectionPotential(Word word, Word firstAnchor, List<Word> allWords)
    {
        double score = ScoreAnchorWordWithIntersectionPotential(word, allWords);
        
        // Bonus for having multiple shared letters with first anchor
        var sharedLetters = word.Text.Intersect(firstAnchor.Text).Count();
        score += sharedLetters * 3;
        
        // Additional bonus for different unique letters (more grid coverage)
        var newLetters = word.Text.Except(firstAnchor.Text).Distinct().Count();
        score += newLetters * 1.5;
        
        return score;
    }

    // Keep original for backwards compatibility but mark as deprecated
    private double ScoreAnchorWord(Word word)
    {
        double score = 0;
        score += word.Text.Count(c => "AEIOU".Contains(c)) * 3;
        score += word.Text.Count(c => "RNSTL".Contains(c)) * 2;
        score += word.Text.Count(c => "ÅÄÖ".Contains(c)) * 1;
        if (word.Length >= 5 && word.Length <= 8)
            score += 5;
        score += word.Text.Distinct().Count() * 0.5;
        return score;
    }

    private double ScoreSecondAnchor(Word word, Word firstAnchor)
    {
        double score = ScoreAnchorWord(word);
        var sharedLetters = word.Text.Intersect(firstAnchor.Text).Count();
        score += sharedLetters * 3;
        return score;
    }

    private double ScoreAnchorIntersection((int Row, int Column, Direction Direction, Word IntersectingWord, int MyIndex, int TheirIndex) intersection, CrosswordGrid grid)
    {
        var (row, col, direction, _, myIndex, _) = intersection;
        
        double score = 1.0;
        
        // Prefer intersections near the middle of the word
        var distanceFromMiddle = Math.Abs(myIndex - intersection.IntersectingWord.Length / 2.0);
        score += (5 - distanceFromMiddle) * 0.5;
        
        // Prefer positions near center of grid
        var centerDistance = Math.Abs(row - grid.Height / 2.0) + Math.Abs(col - grid.Width / 2.0);
        score -= centerDistance * 0.1;
        
        return score;
    }

    #endregion

    #region Word Analysis

    private List<WordAnalysis> AnalyzeWordConnectivity(List<Word> words)
    {
        var analysis = new List<WordAnalysis>();
        
        foreach (var word in words)
        {
            var connectivityScore = CalculateConnectivityScore(word, words);
            analysis.Add(new WordAnalysis
            {
                Word = word,
                ConnectivityScore = connectivityScore,
                VowelCount = word.Text.Count(c => "AEIOUÅÄÖ".Contains(c)),
                CommonLetterCount = word.Text.Count(c => "RNSTLKAEIOUÅÄÖ".Contains(c))
            });
        }

        return analysis;
    }

    private double CalculateConnectivityScore(Word targetWord, List<Word> allWords)
    {
        var score = 0.0;
        var letterFreq = new Dictionary<char, int>();
        
        foreach (var c in targetWord.Text)
        {
            letterFreq[c] = letterFreq.GetValueOrDefault(c, 0) + 1;
        }

        foreach (var otherWord in allWords)
        {
            if (otherWord == targetWord) continue;
            
            foreach (var c in targetWord.Text)
            {
                if (otherWord.Text.Contains(c))
                {
                    score += 1.0 / Math.Sqrt(letterFreq[c]);
                }
            }
        }

        score += targetWord.Text.Count(c => "RNSTL".Contains(c)) * 0.5;
        score += targetWord.Text.Count(c => "AEIOU".Contains(c)) * 0.3;
        score += targetWord.Text.Count(c => "ÅÄÖ".Contains(c)) * 0.2;

        if (targetWord.Length > 8) score *= 0.8;
        
        return score;
    }

    #endregion

    #region Adaptive Placement with Direction Balancing

    private async Task PlaceWordsAdaptivelyWithValidation(CrosswordGrid grid, List<Word> sortedWords, 
        HashSet<Word> placedWords, HashSet<string> usedWordTexts, CrosswordGenerationOptions options, CancellationToken cancellationToken)
    {
        const int maxConsecutiveFailures = 50;
        const int maxPlacementAttempts = 2000;
        
        var placementAttempts = 0;
        var currentTargetLength = options.MaxWordLength;
        var consecutiveFailures = 0;
        var triedWords = new HashSet<string>();
        bool requireIntersections = placedWords.Count > 0;

        while (placementAttempts < maxPlacementAttempts && 
               currentTargetLength >= options.MinWordLength && 
               !cancellationToken.IsCancellationRequested)
        {
            var availableWords = sortedWords
                .Except(placedWords)
                .Where(w => !usedWordTexts.Contains(w.Text)) // Exclude words with same text
                .Where(w => w.Length == currentTargetLength || 
                           (currentTargetLength >= 5 && w.Length >= currentTargetLength - 1 && w.Length <= currentTargetLength + 1))
                .Where(w => !triedWords.Contains(w.Text))
                .OrderBy(w => Math.Abs(w.Length - currentTargetLength))
                .ThenByDescending(w => w.Text.Count(c => "AEIOUÅÄÖ".Contains(c)))
                .ToList();

            if (!availableWords.Any())
            {
                currentTargetLength--;
                consecutiveFailures = 0;
                triedWords.Clear();
                continue;
            }

            placementAttempts++;

            // Direction-aware word selection
            var word = SelectBestWordWithDirectionBalance(availableWords, grid, requireIntersections);
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
                // Get intersections with direction preference
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

                foreach (var scoredIntersection in intersections)
                {
                    var (row, col, direction, _, _, _) = scoredIntersection.Intersection;
                    if (grid.TryPlaceWordWithValidation(word, row, col, direction, _dictionary, options.RejectInvalidWords))
                    {
                        placedWords.Add(word);
                        usedWordTexts.Add(word.Text); // Track the word text
                        placed = true;
                        consecutiveFailures = 0;
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
                        usedWordTexts.Add(word.Text); // Track the word text
                        placed = true;
                        consecutiveFailures = 0;
                        requireIntersections = true;
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
        if (placedWords.Any())
        {
            var finalStats = grid.GetStats();
            var avgWordLength = placedWords.Average(w => w.Length);
            Console.WriteLine($"Adaptiv placering: {finalStats.FillPercentage:F1}% fyllnad, {placedWords.Count} ord (snitt: {avgWordLength:F1})");
        }
    }

    private Direction GetPreferredDirection(CrosswordGrid grid)
    {
        var acrossCount = grid.Words.Count(w => w.Direction == Direction.Across);
        var downCount = grid.Words.Count(w => w.Direction == Direction.Down);
        
        // Return the direction we need more of
        return acrossCount <= downCount ? Direction.Across : Direction.Down;
    }

    private Word? SelectBestWordWithDirectionBalance(List<Word> availableWords, CrosswordGrid grid, bool requireIntersections)
    {
        if (!availableWords.Any()) return null;

        var preferredDirection = GetPreferredDirection(grid);
        
        var scored = availableWords.Take(25).Select(word =>  // Increased from 20 for more variety
        {
            var intersections = requireIntersections ? grid.GetPossibleIntersections(word).ToList() : [];
            var preferredDirectionIntersections = intersections.Count(i => i.Direction == preferredDirection);
            
            return new
            {
                Word = word,
                IntersectionCount = requireIntersections ? intersections.Count : 1,
                PreferredDirectionCount = preferredDirectionIntersections,
                Score = CalculateAdaptiveWordScore(word, grid, requireIntersections) 
                      + preferredDirectionIntersections * 2
                      + _random.NextDouble() * 3 // Add randomness to selection
            };
        })
        .Where(w => !requireIntersections || w.IntersectionCount > 0)
        .OrderByDescending(w => w.Score)
        .ToList();

        // Pick from top candidates with some randomness
        if (scored.Count > 3)
        {
            var pickIndex = _random.NextDouble() < 0.7 ? 0 : _random.Next(1, Math.Min(4, scored.Count));
            return scored[pickIndex].Word;
        }

        return scored.FirstOrDefault()?.Word;
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

    private double CalculateAdaptiveWordScore(Word word, CrosswordGrid grid, bool requireIntersections)
    {
        var score = 0.0;
        
        score += word.Length * 1.5;
        
        if (requireIntersections)
        {
            var intersectionCount = grid.GetPossibleIntersections(word).Count();
            score += intersectionCount * 3;
            score += word.Text.Count(c => "AEIOUÅÄÖ".Contains(c)) * 0.5;
            score += word.Text.Count(c => "RNSTL".Contains(c)) * 0.3;
        }
        
        if (word.Length > 10) score -= 2;
        score += _random.NextDouble() * 0.5;
        
        return score;
    }

    private double ScoreIntersectionAdaptive((int Row, int Column, Direction Direction, Word IntersectingWord, int MyIndex, int TheirIndex) intersection, CrosswordGrid grid, int wordLength)
    {
        var (row, col, direction, intersectingWord, myIndex, theirIndex) = intersection;
        
        var score = 1.0;
        var sharedLetter = intersectingWord.GetCharAt(theirIndex);
        
        if ("AEIOU".Contains(sharedLetter)) score += 0.5;
        if ("RNSTL".Contains(sharedLetter)) score += 0.3;
        
        var distanceFromEnd = Math.Min(myIndex, wordLength - myIndex - 1);
        score += distanceFromEnd * 0.2;
        
        var surroundingWords = CountNearbyWords(grid, row, col, 3);
        score -= surroundingWords * 0.15;
        
        if (intersectingWord.Length >= 6) score += 0.4;
        
        return score;
    }

    #endregion

    #region Helper Methods

    private int CountNearbyWords(CrosswordGrid grid, int centerRow, int centerCol, int radius)
    {
        var count = 0;
        for (int r = Math.Max(0, centerRow - radius); r <= Math.Min(grid.Height - 1, centerRow + radius); r++)
        {
            for (int c = Math.Max(0, centerCol - radius); c <= Math.Min(grid.Width - 1, centerCol + radius); c++)
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

    private void ReportGenerationResults(CrosswordValidationResult validation, int usedWordCount)
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
        TargetFillPercentage = 45.0,
        Difficulty = null,
        MaxAttempts = 80,
        RejectInvalidWords = true
    };

    public static CrosswordGenerationOptions Hard => new()
    {
        Width = 19,
        Height = 19,
        MinWordLength = 1,
        MaxWordLength = 19,
        TargetFillPercentage = 45.0,
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
        
        // Validate with dictionary if provided to properly number accidental words
        if (dictionary != null)
        {
            Grid.IncludeValidAccidentalWords(dictionary);
            ValidationResult = Grid.ValidateCrossword(dictionary);
        }
        else
        {
            ValidationResult = Grid.ValidateCrossword();
        }
    }

    public void UpdateValidation(SwedishDictionary dictionary)
    {
        Grid.IncludeValidAccidentalWords(dictionary);
        ValidationResult = Grid.ValidateCrossword(dictionary);
    }

    public (List<Word> Across, List<Word> Down) GetClues()
    {
        return Grid.GetWordsByDirection();
    }

    public string ToPuzzleString()
    {
        return Grid.ToDisplayString(showNumbers: true, showSolution: false);
    }

    public string ToSolutionString()
    {
        return Grid.ToDisplayString(showNumbers: true, showSolution: true);
    }

    public string GetCluesString()
    {
        var (across, down) = GetClues();
        var result = new System.Text.StringBuilder();

        if (across.Count > 0)
        {
            result.AppendLine("VÅGRÄTT:");
            foreach (var word in across.OrderBy(w => w.Number))
            {
                result.AppendLine($"{word.Number,2}. {word.Clue}");
            }
        }

        if (down.Count > 0)
        {
            if (across.Count > 0) result.AppendLine();
            result.AppendLine("LODRÄTT:");
            foreach (var word in down.OrderBy(w => w.Number))
            {
                result.AppendLine($"{word.Number,2}. {word.Clue}");
            }
        }

        return result.ToString();
    }
}