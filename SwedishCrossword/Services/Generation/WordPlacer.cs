namespace SwedishCrossword.Services.Generation;

using SwedishCrossword.Models;
using static GenerationHelpers;

/// <summary>
/// Handles anchor word selection and adaptive word placement during generation.
/// </summary>
internal class WordPlacer(SwedishDictionary dictionary, Random random, VinkelordPlacer? vinkelordPlacer = null)
{
    private readonly SwedishDictionary _dictionary = dictionary;
    private readonly Random _random = random;
    private readonly VinkelordPlacer? _vinkelordPlacer = vinkelordPlacer;

    public bool PlaceAnchorWordsWithValidation(CrosswordGrid grid, List<Word> sortedWords, List<Word> allWords,
        CrosswordGenerationOptions options)
    {
        var placed = 0;
        var usedWordTexts = grid.GetPlacedWordTexts();

        var maxAnchorLength = options.Width;
        var minAnchorLength = Math.Max(1, Math.Min(14, options.Width) - 3);

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
            score += _random.NextDouble() * 4.0;
            anchorCandidates.Add((w, score));
        }

        anchorCandidates.Sort((a, b) => b.Score.CompareTo(a.Score));

        Word? bestAnchor = null;
        if (anchorCandidates.Count > 0)
        {
            var pickIndex = _random.Next(Math.Min(30, anchorCandidates.Count));
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

        // Place additional cross-anchors (2nd and 3rd) to create a richer initial scaffold
        if (placed > 0 && sortedWords.Count > 1)
        {
            var targetCrossAnchors = Math.Min(3, 1 + options.Width / 6); // 2 for small grids, 3 for larger

            while (placed < targetCrossAnchors)
            {
                var anchorLetters = new HashSet<char>();
                foreach (var w in grid.Words)
                    foreach (var c in w.Text)
                        anchorLetters.Add(c);

                bool placedOne = false;

                var filteredNextCandidates = new List<Word>(100);
                foreach (var w in allWords)
                {
                    if (w.Length < minAnchorLength || w.Length > maxAnchorLength) continue;
                    if (usedWordTexts.Contains(w.Text)) continue;

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
                        filteredNextCandidates.Add(w);
                }

                if (filteredNextCandidates.Count > 0)
                {
                    var candidateNextWords = new List<(Word Word, double Score)>(filteredNextCandidates.Count);
                    foreach (var w in filteredNextCandidates)
                    {
                        var score = ScoreAnchorWordWithIntersectionPotential(w, letterWordCount);
                        var sharedLetters = w.Text.Intersect(anchorLetters).Count();
                        score += sharedLetters * 3;
                        var newLetters = w.Text.Except(anchorLetters).Distinct().Count();
                        score += newLetters * 1.5;
                        score += _random.NextDouble() * 4.0;
                        candidateNextWords.Add((w, score));
                    }

                    candidateNextWords.Sort((a, b) => b.Score.CompareTo(a.Score));
                    var topCount = Math.Min(40, candidateNextWords.Count);

                    var shuffledCandidates = new List<Word>(topCount);
                    for (int i = 0; i < topCount; i++)
                    {
                        shuffledCandidates.Add(candidateNextWords[i].Word);
                    }

                    ShuffleTopBiased(shuffledCandidates, 10, _random);

                    foreach (var nextWord in shuffledCandidates)
                    {
                        var intersections = grid.GetPossibleIntersections(nextWord)
                            .Select(i => (Intersection: i, Score: ScoreAnchorIntersection(i, grid)))
                            .OrderByDescending(x => x.Score)
                            .Take(8)
                            .ToList();

                        ShuffleTopBiased(intersections, 3, _random);
                        var tryCount = Math.Min(5, intersections.Count);

                        for (int i = 0; i < tryCount; i++)
                        {
                            var (row, col, direction, _, _, _) = intersections[i].Intersection;
                            if (grid.TryPlaceWordWithValidation(nextWord, row, col, direction, _dictionary, options.RejectInvalidWords))
                            {
                                placed++;
                                usedWordTexts = grid.GetPlacedWordTexts();
                                placedOne = true;
                                break;
                            }
                        }

                        if (placedOne) break;
                    }
                }

                // If we couldn't place another anchor, stop trying
                if (!placedOne) break;
            }
        }

        return placed > 0;
    }

    /// <summary>
    /// Attempts to place a vinkelord (bent word) as a cross-anchor using
    /// opportunity-based placement (existing L-shapes with letters on the grid).
    /// Returns false if no valid opportunity-based placement is found � callers
    /// should NOT force a vinkelord when the grid layout doesn't support one,
    /// as poorly positioned L-shapes fragment the grid and reduce fill quality.
    /// </summary>
    private bool TryPlaceVinkelordAnchor(CrosswordGrid grid, List<Word> allWords,
        HashSet<string> usedWordTexts, CrosswordGenerationOptions options)
    {
        var maxVinkelordLength = Math.Min(options.MaxWordLength, options.MaxVinkelordLength);
        var opportunities = _vinkelordPlacer!.FindVinkelordOpportunities(
            grid, minLength: 3, maxLength: maxVinkelordLength, maxBends: options.MaxBendsPerWord);

        if (opportunities.Count == 0) return false;

        var candidatePool = _dictionary.GetWords(minLength: 3, maxLength: maxVinkelordLength).ToList();
        var placedTexts = new HashSet<string>(usedWordTexts, StringComparer.OrdinalIgnoreCase);

        var limit = Math.Min(15, opportunities.Count);
        for (int oppIdx = 0; oppIdx < limit; oppIdx++)
        {
            var opportunity = opportunities[oppIdx];
            var matchingWords = FindWordsMatchingPattern(candidatePool, opportunity.Pattern, placedTexts, usedWordTexts);
            if (matchingWords.Count == 0) continue;

            var scored = new List<(Word Word, double Score)>(matchingWords.Count);
            foreach (var w in matchingWords)
            {
                double score = opportunity.ExistingLetterCount * 5.0;
                foreach (var c in w.Text)
                {
                    if (c is 'A' or 'E' or 'I' or 'O' or 'U' or '\u00c5' or '\u00c4' or '\u00d6')
                        score += 0.5;
                    else if (c is 'R' or 'N' or 'S' or 'T' or 'L')
                        score += 0.3;
                }
                score += _random.NextDouble() * 2.0;
                scored.Add((w, score));
            }

            scored.Sort((a, b) => b.Score.CompareTo(a.Score));
            if (scored.Count > 8) scored.RemoveRange(8, scored.Count - 8);
            ShuffleTopBiased(scored, 3, _random);

            var tryCount = Math.Min(5, scored.Count);
            for (int i = 0; i < tryCount; i++)
            {
                if (grid.TryPlaceBentWordWithValidation(scored[i].Word, opportunity.Segments, _dictionary, options.RejectInvalidWords))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to place a vinkelord at an arbitrary grid position without requiring
    /// pre-existing L-shaped opportunities. Picks a word first, then finds valid
    /// L-shaped placements for it. Works even on an empty grid.
    /// </summary>
    private bool TryPlaceFreeVinkelord(CrosswordGrid grid, HashSet<string> usedWordTexts,
        CrosswordGenerationOptions options)
    {
        var maxVinkelordLength = Math.Min(options.MaxWordLength, options.MaxVinkelordLength);

        var candidatePool = _dictionary.GetWords(minLength: 3, maxLength: maxVinkelordLength)
            .Where(w => !usedWordTexts.Contains(w.Text))
            .Take(2000)
            .ToList();

        if (candidatePool.Count == 0) return false;

        // Score candidates for anchor quality (without the >= 15 length penalty
        // since vinkelords can naturally span longer across two dimensions)
        var scored = new List<(Word Word, double Score)>(candidatePool.Count);
        foreach (var w in candidatePool)
        {
            double score = 0;
            var uniqueLetters = new HashSet<char>();
            foreach (var c in w.Text)
            {
                uniqueLetters.Add(c);
                if (c is 'A' or 'E' or 'I' or 'O' or 'U' or '\u00c5' or '\u00c4' or '\u00d6')
                    score += 1.0;
                else if (c is 'R' or 'N' or 'S' or 'T' or 'L')
                    score += 0.7;
            }
            if (w.Length >= 5 && w.Length <= 10) score += 3;
            score += uniqueLetters.Count * 0.5;
            var lengthJitter = 4.0 + Math.Max(0, w.Length - 6) * 0.8;
            score += _random.NextDouble() * lengthJitter;
            scored.Add((w, score));
        }

        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        ShuffleTopBiased(scored, 10, _random);

        var tryWords = Math.Min(15, scored.Count);
        for (int wi = 0; wi < tryWords; wi++)
        {
            var word = scored[wi].Word;
            var positions = _vinkelordPlacer!.GenerateFreeVinkelordPositions(grid, word.Length);
            if (positions.Count == 0) continue;

            ShuffleTopBiased(positions, 5, _random);
            var tryPositions = Math.Min(8, positions.Count);

            for (int pi = 0; pi < tryPositions; pi++)
            {
                if (grid.TryPlaceBentWordWithValidation(word, positions[pi], _dictionary, options.RejectInvalidWords))
                    return true;
            }
        }

        return false;
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
            else if (c is '�' or '�' or '�')
                score += 0.5;
        }

        if (word.Length >= 6 && word.Length <= 9)
            score += 3;
        if (word.Length >= 5 && word.Length <= 8)
            score += 2;

        score += uniqueLetters.Count * 0.5;

        int intersectionPotential = 0;
        foreach (var letter in uniqueLetters)
        {
            if (letterWordCount.TryGetValue(letter, out var count))
            {
                intersectionPotential += count - 1;
            }
        }
        score += intersectionPotential / 500.0;

        // Gentle taper beyond the typical anchor range (15+).  Words 11-14
        // are the anchor sweet-spot and should keep their full score; only the
        // longer words that were added by the expanded maxAnchorLength get a
        // gradual penalty instead of the original hard cliff at 15.
        if (word.Length > 14)
            score *= Math.Max(0.3, 1.0 - (word.Length - 14) * 0.10);

        return score;
    }

    private double ScoreAnchorIntersection((int Row, int Column, Direction Direction, Word IntersectingWord, int MyIndex, int TheirIndex) intersection, CrosswordGrid grid)
    {
        var (row, col, direction, _, myIndex, _) = intersection;

        double score = 1.0;

        var halfLength = intersection.IntersectingWord.Length / 2.0;
        var distanceFromMiddle = Math.Abs(myIndex - halfLength);
        score += Math.Max(0, halfLength - distanceFromMiddle) * 0.5;

        var centerDistance = Math.Abs(row - grid.Height / 2.0) + Math.Abs(col - grid.Width / 2.0);
        score -= centerDistance * 0.1;

        return score;
    }

    /// <summary>
    /// Persistent state for adaptive word placement that survives across batched calls.
    /// Created once by the orchestrator and passed into each batch invocation so that
    /// currentTargetLength, placementAttempts, triedWords, etc. are not reset.
    /// </summary>
    internal class AdaptivePlacementState
    {
        public int PlacementAttempts;
        public int CurrentTargetLength;
        public int ConsecutiveFailures;
        public HashSet<string> TriedWords = new();
        public bool RequireIntersections;
        public HashSet<string> PlacedWordTexts;
        public HashSet<string> UsedWordTexts;
        public int UsedWordsRefreshCounter;
        public bool IsExhausted;
        public int VinkelordPlaced;
        public List<Word>? VinkelordCandidatePool;

        public AdaptivePlacementState(CrosswordGenerationOptions options, HashSet<Word> placedWords, CrosswordGrid grid)
        {
            CurrentTargetLength = options.MaxWordLength;
            RequireIntersections = placedWords.Count > 0;
            PlacedWordTexts = new HashSet<string>(placedWords.Select(w => w.Text), StringComparer.OrdinalIgnoreCase);
            UsedWordTexts = grid.GetPlacedWordTexts();
        }
    }

    /// <summary>
    /// Creates a new adaptive placement state. Call once before the first batch.
    /// </summary>
    public AdaptivePlacementState CreateAdaptiveState(CrosswordGenerationOptions options, HashSet<Word> placedWords, CrosswordGrid grid)
    {
        return new AdaptivePlacementState(options, placedWords, grid);
    }

    /// <summary>
    /// Adaptive word placement that can be run in bounded batches.
    /// When maxWordsPerBatch > 0, stops after placing that many words.
    /// State is preserved in the AdaptivePlacementState object across calls.
    /// Returns the number of words placed in this batch.
    /// </summary>
    public async Task<int> PlaceWordsAdaptivelyWithValidation(CrosswordGrid grid, List<Word> sortedWords,
        HashSet<Word> placedWords, CrosswordGenerationOptions options, Dictionary<string, double> placedWordScores,
        Dictionary<string, double> connectivityScores,
        CancellationToken cancellationToken, AdaptivePlacementState state, int maxWordsPerBatch = 0)
    {
        const int maxConsecutiveFailures = 100;
        const int maxPlacementAttempts = 4000;

        if (state.IsExhausted)
            return 0;

        int wordsPlacedThisBatch = 0;

        while (state.PlacementAttempts < maxPlacementAttempts &&
               state.CurrentTargetLength >= options.MinWordLength &&
               !cancellationToken.IsCancellationRequested)
        {
            // Yield control after placing the requested batch size
            if (maxWordsPerBatch > 0 && wordsPlacedThisBatch >= maxWordsPerBatch)
            {
                return wordsPlacedThisBatch;
            }

            if (state.UsedWordsRefreshCounter >= 20)
            {
                state.UsedWordTexts = grid.GetPlacedWordTexts();
                state.UsedWordsRefreshCounter = 0;
            }

            state.PlacementAttempts++;
            state.UsedWordsRefreshCounter++;

            var placed = false;

            // Vinkelord eligibility -- used only by the reactive fallback below,
            // after straight placement has failed enough times.  Proactive
            // vinkelord in the main loop was removed because early L-shapes
            // fragment the grid and choke straight-word placement.
            bool vinkelordEligible = _vinkelordPlacer != null && options.AllowVinkelord
                && state.VinkelordPlaced < options.MaxVinkelord;

            // Straight word selection and placement
            var lengthMin = Math.Max(options.MinWordLength, state.CurrentTargetLength - 2);
            var availableWords = sortedWords
                .Where(w => !state.PlacedWordTexts.Contains(w.Text)
                         && !state.UsedWordTexts.Contains(w.Text)
                         && !state.TriedWords.Contains(w.Text)
                         && w.Length >= lengthMin && w.Length <= state.CurrentTargetLength)
                .OrderBy(w => Math.Abs(w.Length - state.CurrentTargetLength))
                .ThenByDescending(w => CountVowels(w.Text))
                .Take(100)
                .ToList();

            if (availableWords.Count == 0)
            {
                state.CurrentTargetLength--;
                state.ConsecutiveFailures = 0;
                state.TriedWords.Clear();
                continue;
            }

            var (word, wordScore) = SelectBestWordWithDirectionBalanceAndScore(availableWords, grid, state.RequireIntersections, connectivityScores);
            if (word == null)
            {
                state.CurrentTargetLength--;
                state.ConsecutiveFailures = 0;
                state.TriedWords.Clear();
                continue;
            }

            if (state.RequireIntersections)
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

                ShuffleTopBiased(intersections, 3, _random);
                var tryCount = Math.Min(8, intersections.Count);

                for (int i = 0; i < tryCount; i++)
                {
                    var (row, col, direction, _, _, _) = intersections[i].Intersection;
                    if (grid.TryPlaceWordWithValidation(word, row, col, direction, _dictionary, options.RejectInvalidWords))
                    {
                        placedWords.Add(word);
                        state.PlacedWordTexts.Add(word.Text);
                        state.UsedWordTexts.Add(word.Text);
                        placedWordScores[word.Text] = connectivityScores.GetValueOrDefault(word.Text);
                        placed = true;
                        state.ConsecutiveFailures = 0;
                        state.UsedWordsRefreshCounter = 20;
                        wordsPlacedThisBatch++;
                        break;
                    }
                }
            }

            if (!placed && !state.RequireIntersections)
            {
                var freePositions = FindOptimalFreePositions(grid, word).Take(5).ToList();
                foreach (var (row, col, direction) in freePositions)
                {
                    if (grid.TryPlaceWordWithValidation(word, row, col, direction, _dictionary, options.RejectInvalidWords))
                    {
                        placedWords.Add(word);
                        state.PlacedWordTexts.Add(word.Text);
                        state.UsedWordTexts.Add(word.Text);
                        placedWordScores[word.Text] = connectivityScores.GetValueOrDefault(word.Text);
                        placed = true;
                        state.ConsecutiveFailures = 0;
                        state.RequireIntersections = true;
                        state.UsedWordsRefreshCounter = 20;
                        wordsPlacedThisBatch++;
                        break;
                    }
                }
            }

            if (!placed)
            {
                state.ConsecutiveFailures++;
                state.TriedWords.Add(word.Text);

                // Try vinkelord as alternative strategy every 10 consecutive failures
                if (vinkelordEligible && state.ConsecutiveFailures % 10 == 0)
                {
                    if (TryPlaceOneVinkelord(grid, placedWords, options, state, placedWordScores, connectivityScores, cancellationToken))
                    {
                        state.ConsecutiveFailures = 0;
                        state.UsedWordsRefreshCounter = 20;
                        wordsPlacedThisBatch++;
                    }
                }

                if (state.ConsecutiveFailures >= maxConsecutiveFailures)
                {
                    state.CurrentTargetLength--;
                    state.ConsecutiveFailures = 0;
                    state.TriedWords.Clear();
                }
            }
        }

        state.IsExhausted = true;

        // Only log when words were actually placed in THIS batch (not cumulative)
        if (wordsPlacedThisBatch > 0)
        {
            var finalStats = grid.GetStats();
            Console.WriteLine($"Adaptiv placering: +{wordsPlacedThisBatch} ord, {finalStats.FillPercentage:F1}% fyllnad");
        }

        return wordsPlacedThisBatch;
    }

    /// <summary>
    /// Attempts to place a single vinkelord (bent word) by scanning L-shaped
    /// opportunities and matching them against dictionary words.
    /// </summary>
    private bool TryPlaceOneVinkelord(CrosswordGrid grid, HashSet<Word> placedWords,
        CrosswordGenerationOptions options, AdaptivePlacementState state,
        Dictionary<string, double> placedWordScores, Dictionary<string, double> connectivityScores,
        CancellationToken cancellationToken)
    {
        var maxVinkelordLength = Math.Min(options.MaxWordLength, options.MaxVinkelordLength);

        var opportunities = _vinkelordPlacer!.FindVinkelordOpportunities(
            grid, minLength: 3, maxLength: maxVinkelordLength, maxBends: options.MaxBendsPerWord);
        if (opportunities.Count == 0) return false;

        // Lazily build the base candidate pool (dictionary words of valid length)
        state.VinkelordCandidatePool ??= _dictionary.GetWords(
            minLength: 3, maxLength: maxVinkelordLength).ToList();

        var limit = Math.Min(20, opportunities.Count);
        for (int oppIdx = 0; oppIdx < limit; oppIdx++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var opportunity = opportunities[oppIdx];
            var matchingWords = FindWordsMatchingPattern(
                state.VinkelordCandidatePool, opportunity.Pattern, state.PlacedWordTexts, state.UsedWordTexts);
            if (matchingWords.Count == 0) continue;

            var scored = new List<(Word Word, double Score)>(matchingWords.Count);
            foreach (var w in matchingWords)
            {
                double score = opportunity.ExistingLetterCount * 5.0;
                foreach (var c in w.Text)
                {
                    if (c is 'A' or 'E' or 'I' or 'O' or 'U' or '\u00c5' or '\u00c4' or '\u00d6')
                        score += 0.5;
                    else if (c is 'R' or 'N' or 'S' or 'T' or 'L')
                        score += 0.3;
                }
                scored.Add((w, score));
            }

            scored.Sort((a, b) => b.Score.CompareTo(a.Score));
            if (scored.Count > 8)
                scored.RemoveRange(8, scored.Count - 8);
            ShuffleTopBiased(scored, 3, _random);

            var tryCount = Math.Min(5, scored.Count);
            for (int i = 0; i < tryCount; i++)
            {
                var word = scored[i].Word;
                if (grid.TryPlaceBentWordWithValidation(word, opportunity.Segments, _dictionary, options.RejectInvalidWords))
                {
                    placedWords.Add(word);
                    state.PlacedWordTexts.Add(word.Text);
                    state.UsedWordTexts.Add(word.Text);
                    placedWordScores[word.Text] = connectivityScores.GetValueOrDefault(word.Text);
                    state.VinkelordPlaced++;
                    return true;
                }
            }
        }

        return false;
    }

    private (Word? Word, double Score) SelectBestWordWithDirectionBalanceAndScore(List<Word> availableWords, CrosswordGrid grid, bool requireIntersections, Dictionary<string, double> connectivityScores)
    {
        if (availableWords.Count == 0) return (null, 0);

        var preferredDirection = GetPreferredDirection(grid);

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

                if (intersectionCount == 0) continue;
            }
            else
            {
                intersectionCount = 1;
            }

            var score = ScoreCandidateWord(word, intersectionCount, requireIntersections, connectivityScores)
                      + preferredDirectionIntersections * 2;

            scored.Add((word, intersectionCount, preferredDirectionIntersections, score));
        }

        if (scored.Count == 0) return (null, 0);

        scored.Sort((a, b) => b.Score.CompareTo(a.Score));

        var pickRange = Math.Min(4, scored.Count);
        var pickIndex = _random.Next(pickRange);
        var selected = scored[pickIndex];
        return (selected.Word, selected.Score);
    }

    /// <summary>
    /// Scores a candidate word for adaptive placement. Uses the pre-computed
    /// ConnectivityScore for static word quality (letter frequency, length penalties)
    /// and adds only the grid-specific intersection density that requires live state.
    /// </summary>
    private static double ScoreCandidateWord(Word word, int intersectionCount, bool requireIntersections, Dictionary<string, double> connectivityScores)
    {
        // Static word quality from pre-computed analysis
        var score = connectivityScores.GetValueOrDefault(word.Text) * 10;

        // Grid-specific: intersection density (only known at placement time)
        if (requireIntersections)
            score += (intersectionCount / (double)word.Length) * 10;

        return score;
    }

    private double ScoreIntersectionAdaptive((int Row, int Column, Direction Direction, Word IntersectingWord, int MyIndex, int TheirIndex) intersection, CrosswordGrid grid, int wordLength)
    {
        var (row, col, direction, intersectingWord, myIndex, theirIndex) = intersection;

        var score = 1.0;
        var sharedLetter = intersectingWord.GetCharAt(theirIndex);

        if (sharedLetter is 'A' or 'E' or 'I' or 'O' or 'U') score += 0.5;
        else if (sharedLetter is 'R' or 'N' or 'S' or 'T' or 'L') score += 0.3;

        var distanceFromEnd = Math.Min(myIndex, wordLength - myIndex - 1);
        score += distanceFromEnd * 0.2;

        var surroundingWords = CountNearbyWords(grid, row, col, 3);
        score -= surroundingWords * 0.15;

        if (intersectingWord.Length >= 6) score += 0.4;

        return score;
    }

    private double ScoreIntersectionWithDirectionBonus(
        (int Row, int Column, Direction Direction, Word IntersectingWord, int MyIndex, int TheirIndex) intersection,
        CrosswordGrid grid, int wordLength, Direction preferredDirection)
    {
        var score = ScoreIntersectionAdaptive(intersection, grid, wordLength);

        if (intersection.Direction == preferredDirection)
            score += 3;

        return score;
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
                        var score = ScoreFreePosition(grid, row, col, dir);
                        positions.Add((row, col, dir, score));
                    }
                }
            }
        }

        return positions.OrderByDescending(p => p.Score).Select(p => (p.Row, p.Column, p.Direction));
    }

    private double ScoreFreePosition(CrosswordGrid grid, int row, int col, Direction direction)
    {
        var score = 0.0;

        var centerDistance = Math.Sqrt(Math.Pow(row - grid.Height / 2.0, 2) + Math.Pow(col - grid.Width / 2.0, 2));
        score -= centerDistance * 0.1;

        var preferredDirection = GetPreferredDirection(grid);
        if (direction == preferredDirection)
            score += 1.0;

        return score;
    }
}
