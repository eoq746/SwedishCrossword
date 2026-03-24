namespace SwedishCrossword.Services.Generation;

using SwedishCrossword.Models;
using static GenerationHelpers;

/// <summary>
/// Handles vinkelord (bent word) opportunity detection and placement.
/// </summary>
internal class VinkelordPlacer(SwedishDictionary dictionary, Random random)
{
    private readonly SwedishDictionary _dictionary = dictionary;
    private readonly Random _random = random;

    /// <summary>
    /// Finds L-shaped (and multi-bend) opportunities where a word could be placed
    /// by combining a horizontal and vertical run that share a corner cell.
    /// </summary>
    public List<VinkelordOpportunity> FindVinkelordOpportunities(CrosswordGrid grid, int minLength = 3, int maxLength = 20, int maxBends = 1)
    {
        var opportunities = new List<VinkelordOpportunity>();

        for (int bendRow = 0; bendRow < grid.Height; bendRow++)
        {
            for (int bendCol = 0; bendCol < grid.Width; bendCol++)
            {
                var bendCell = grid.GetCell(bendRow, bendCol);
                if (bendCell.IsBlocked) continue;

                // Skip cells that already carry a bend arrow from another vinkelord.
                // Placing a second bend here would overwrite the first arrow and make
                // two separate vinkelord look like a single word with two bends.
                if (bendCell.BendArrowDirection != null) continue;

                // A bend places a direction arrow on the bend cell. Skip cells that are
                // the terminal cell of any existing word: the arrow would make that word
                // appear to continue past its actual end.
                bool isTerminalOfExistingWord = false;
                foreach (var w in grid.Words)
                {
                    if (w.IsPlaced && w.EndRow == bendRow && w.EndColumn == bendCol)
                    {
                        isTerminalOfExistingWord = true;
                        break;
                    }
                }
                if (isTerminalOfExistingWord) continue;

                TryBuildLShape(grid, bendRow, bendCol, Direction.Across, Direction.Down, minLength, maxLength, opportunities);
                TryBuildLShape(grid, bendRow, bendCol, Direction.Down, Direction.Across, minLength, maxLength, opportunities);
            }
        }

        // Sort by matchability: prefer shorter patterns that are more likely to
        // find matching dictionary words. Longer L-shapes combine letters from two
        // different words and are almost never real words.
        opportunities.Sort((a, b) =>
        {
            double ScoreOpportunity(VinkelordOpportunity opp)
            {
                // Strong preference for shorter total lengths — these are the ones
                // that actually match dictionary words
                double score = 20.0 - opp.TotalLength;

                // Reward having existing letters (connectivity/intersection)
                score += Math.Min(opp.ExistingLetterCount, 4) * 2.0;

                // Must have at least 1 empty cell (otherwise nothing to fill)
                if (opp.EmptyCellCount == 0) return -100;

                return score;
            }

            return ScoreOpportunity(b).CompareTo(ScoreOpportunity(a));
        });

        return opportunities;
    }

    private void TryBuildLShape(CrosswordGrid grid, int bendRow, int bendCol,
        Direction firstDir, Direction secondDir, int minLength, int maxLength,
        List<VinkelordOpportunity> opportunities)
    {
        var firstLengths = GetSegmentLengths(grid, bendRow, bendCol, firstDir, backward: true, minCells: 2);
        var secondLengths = GetSegmentLengths(grid, bendRow, bendCol, secondDir, backward: false, minCells: 2);

        foreach (int firstLen in firstLengths)
        {
            foreach (int secondLen in secondLengths)
            {
                int totalLength = firstLen + secondLen - 1;
                if (totalLength < minLength || totalLength > maxLength) continue;

                var seg1Start = GetSegmentStart(bendRow, bendCol, firstDir, firstLen);
                var seg1 = new WordSegment
                {
                    StartRow = seg1Start.Row,
                    StartCol = seg1Start.Col,
                    Direction = firstDir,
                    Length = firstLen
                };

                var seg2 = new WordSegment
                {
                    StartRow = bendRow,
                    StartCol = bendCol,
                    Direction = secondDir,
                    Length = secondLen
                };

                var pattern = new List<char?>();
                int existingLetters = 0;
                int emptyCells = 0;
                bool valid = true;

                foreach (var (r, c) in seg1.GetPositions())
                {
                    if (!grid.IsValidPosition(r, c)) { valid = false; break; }
                    var cell = grid.GetCell(r, c);
                    if (cell.IsBlocked) { valid = false; break; }
                    if (cell.HasLetter) { pattern.Add(cell.Letter); existingLetters++; }
                    else { pattern.Add(null); emptyCells++; }
                }

                if (!valid) continue;

                var seg2Positions = seg2.GetPositions().ToList();
                for (int i = 1; i < seg2Positions.Count; i++)
                {
                    var (r, c) = seg2Positions[i];
                    if (!grid.IsValidPosition(r, c)) { valid = false; break; }
                    var cell = grid.GetCell(r, c);
                    if (cell.IsBlocked) { valid = false; break; }
                    if (cell.HasLetter) { pattern.Add(cell.Letter); existingLetters++; }
                    else { pattern.Add(null); emptyCells++; }
                }

                if (!valid) continue;

                if (existingLetters < 1 || emptyCells < 1) continue;

                if (firstDir == Direction.Across)
                {
                    if (seg1.StartCol > 0 && grid.GetCell(seg1.StartRow, seg1.StartCol - 1).HasLetter) continue;
                }
                else
                {
                    if (seg1.StartRow > 0 && grid.GetCell(seg1.StartRow - 1, seg1.StartCol).HasLetter) continue;
                }

                if (secondDir == Direction.Across)
                {
                    if (seg2.EndCol + 1 < grid.Width && grid.GetCell(seg2.EndRow, seg2.EndCol + 1).HasLetter) continue;
                }
                else
                {
                    if (seg2.EndRow + 1 < grid.Height && grid.GetCell(seg2.EndRow + 1, seg2.EndCol).HasLetter) continue;
                }

                opportunities.Add(new VinkelordOpportunity
                {
                    Segments = [seg1, seg2],
                    Pattern = pattern,
                    TotalLength = totalLength,
                    ExistingLetterCount = existingLetters,
                    EmptyCellCount = emptyCells
                });
            }
        }
    }

    private List<int> GetSegmentLengths(CrosswordGrid grid, int bendRow, int bendCol,
        Direction direction, bool backward, int minCells)
    {
        var lengths = new List<int>();

        int dr = direction == Direction.Down ? (backward ? -1 : 1) : 0;
        int dc = direction == Direction.Across ? (backward ? -1 : 1) : 0;

        int maxExtend = 0;
        int r = bendRow + dr;
        int c = bendCol + dc;

        while (grid.IsValidPosition(r, c))
        {
            var cell = grid.GetCell(r, c);
            if (cell.IsBlocked) break;
            maxExtend++;
            r += dr;
            c += dc;
        }

        for (int extend = minCells - 1; extend <= maxExtend; extend++)
        {
            lengths.Add(extend + 1);
        }

        return lengths;
    }

    private static (int Row, int Col) GetSegmentStart(int bendRow, int bendCol, Direction direction, int length)
    {
        if (direction == Direction.Across)
            return (bendRow, bendCol - length + 1);
        else
            return (bendRow - length + 1, bendCol);
    }

    /// <summary>
    /// Generates L-shaped segment configurations for placing a word of the given length
    /// at arbitrary positions on the grid, without requiring pre-existing letters.
    /// This enables vinkelord placement on an empty grid or in empty areas.
    /// Positions are scored by proximity to grid center and segment balance,
    /// then the top candidates are returned.
    /// </summary>
    public List<List<WordSegment>> GenerateFreeVinkelordPositions(
        CrosswordGrid grid, int wordLength, int maxPositions = 30)
    {
        if (wordLength < 3) return [];

        var candidates = new List<(List<WordSegment> Segments, double Score)>();
        double centerRow = grid.Height / 2.0;
        double centerCol = grid.Width / 2.0;

        var terminalCells = new HashSet<(int, int)>();
        foreach (var w in grid.Words)
        {
            if (w.IsPlaced)
                terminalCells.Add((w.EndRow, w.EndColumn));
        }

        ReadOnlySpan<(Direction First, Direction Second)> dirPairs =
        [
            (Direction.Across, Direction.Down),
            (Direction.Down, Direction.Across)
        ];

        for (int seg1Len = 2; seg1Len <= wordLength - 1; seg1Len++)
        {
            int seg2Len = wordLength + 1 - seg1Len;
            if (seg2Len < 2) continue;

            foreach (var (firstDir, secondDir) in dirPairs)
            {
                for (int bendRow = 0; bendRow < grid.Height; bendRow++)
                {
                    for (int bendCol = 0; bendCol < grid.Width; bendCol++)
                    {
                        if (grid.GetCell(bendRow, bendCol).IsBlocked) continue;
                        if (grid.GetCell(bendRow, bendCol).BendArrowDirection != null) continue;
                        if (terminalCells.Contains((bendRow, bendCol))) continue;

                        var seg1Start = GetSegmentStart(bendRow, bendCol, firstDir, seg1Len);

                        var seg1 = new WordSegment
                        {
                            StartRow = seg1Start.Row,
                            StartCol = seg1Start.Col,
                            Direction = firstDir,
                            Length = seg1Len
                        };

                        var seg2 = new WordSegment
                        {
                            StartRow = bendRow,
                            StartCol = bendCol,
                            Direction = secondDir,
                            Length = seg2Len
                        };

                        // Validate all positions are in bounds and not blocked
                        bool valid = true;
                        foreach (var (r, c) in seg1.GetPositions())
                        {
                            if (!grid.IsValidPosition(r, c) || grid.GetCell(r, c).IsBlocked)
                            { valid = false; break; }
                        }
                        if (!valid) continue;

                        var seg2Positions = seg2.GetPositions().ToList();
                        for (int i = 1; i < seg2Positions.Count; i++)
                        {
                            var (r, c) = seg2Positions[i];
                            if (!grid.IsValidPosition(r, c) || grid.GetCell(r, c).IsBlocked)
                            { valid = false; break; }
                        }
                        if (!valid) continue;

                        // Isolation before the first segment
                        if (firstDir == Direction.Across)
                        {
                            if (seg1.StartCol > 0 && grid.GetCell(seg1.StartRow, seg1.StartCol - 1).HasLetter) continue;
                        }
                        else
                        {
                            if (seg1.StartRow > 0 && grid.GetCell(seg1.StartRow - 1, seg1.StartCol).HasLetter) continue;
                        }

                        // Isolation after the last segment
                        if (secondDir == Direction.Across)
                        {
                            if (seg2.EndCol + 1 < grid.Width && grid.GetCell(seg2.EndRow, seg2.EndCol + 1).HasLetter) continue;
                        }
                        else
                        {
                            if (seg2.EndRow + 1 < grid.Height && grid.GetCell(seg2.EndRow + 1, seg2.EndCol).HasLetter) continue;
                        }

                        var dist = Math.Abs(bendRow - centerRow) + Math.Abs(bendCol - centerCol);
                        double score = 20.0 - dist * 0.3;
                        // Prefer balanced L-shapes
                        var splitBalance = 1.0 - Math.Abs(seg1Len - seg2Len) / (double)wordLength;
                        score += splitBalance * 3.0;
                        score += _random.NextDouble() * 2.0;

                        candidates.Add(([seg1, seg2], score));
                    }
                }
            }
        }

        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));

        var result = new List<List<WordSegment>>(Math.Min(maxPositions, candidates.Count));
        for (int i = 0; i < candidates.Count && result.Count < maxPositions; i++)
        {
            result.Add(candidates[i].Segments);
        }

        return result;
    }

    /// <summary>
    /// Attempts to fill vinkelord (bent word) opportunities with matching words.
    /// </summary>
    public async Task FillVinkelordAsync(CrosswordGrid grid, List<Word> candidateWords, HashSet<Word> placedWords,
        CrosswordGenerationOptions options, Dictionary<string, double> placedWordScores,
        Dictionary<string, double> connectivityScores,
        int vinkelordPlaced, CancellationToken cancellationToken)
    {
        if (!options.AllowVinkelord) return;
        if (vinkelordPlaced >= options.MaxVinkelord) return;

        var usedWordTexts = grid.GetPlacedWordTexts();
        var placedWordTexts = new HashSet<string>(placedWords.Select(w => w.Text), StringComparer.OrdinalIgnoreCase);

        // Cap vinkelord length to match actual dictionary word lengths
        var maxVinkelordLength = Math.Min(options.MaxWordLength, options.MaxVinkelordLength);

        var vinkelordCandidates = _dictionary.GetWords(
            minLength: 3,
            maxLength: maxVinkelordLength
        ).Where(w => !placedWordTexts.Contains(w.Text) && !usedWordTexts.Contains(w.Text))
         .ToList();

        int totalProcessed = 0;
        const int maxOpportunitiesPerPass = 500;
        const int maxPassesWithoutPlacement = 5;
        int passesWithoutPlacement = 0;

        while (totalProcessed < maxOpportunitiesPerPass && vinkelordPlaced < options.MaxVinkelord && !cancellationToken.IsCancellationRequested)
        {
            var opportunities = FindVinkelordOpportunities(grid, minLength: 3, maxLength: maxVinkelordLength, maxBends: options.MaxBendsPerWord);
            if (opportunities.Count == 0) break;

            bool placedAny = false;
            var limit = Math.Min(maxOpportunitiesPerPass - totalProcessed, opportunities.Count);

            for (int oppIdx = 0; oppIdx < limit; oppIdx++)
            {
                if (cancellationToken.IsCancellationRequested) break;
                totalProcessed++;

                var opportunity = opportunities[oppIdx];
                var matchingWords = FindWordsMatchingPattern(vinkelordCandidates, opportunity.Pattern, placedWordTexts, usedWordTexts);
                if (matchingWords.Count == 0) continue;

                var scored = new List<(Word Word, double Score)>(matchingWords.Count);
                foreach (var w in matchingWords)
                {
                    double score = opportunity.ExistingLetterCount * 5.0;
                    foreach (var c in w.Text)
                    {
                        if (c is 'A' or 'E' or 'I' or 'O' or 'U' or 'Å' or 'Ä' or 'Ö')
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
                        placedWordTexts.Add(word.Text);
                        usedWordTexts.Add(word.Text);
                        placedWordScores[word.Text] = connectivityScores.GetValueOrDefault(word.Text);
                        vinkelordPlaced++;
                        placedAny = true;
                        //Console.WriteLine($"    Vinkelord placerat: {word.Text} ({word.BendCount} böj)");
                        break;
                    }
                }

                if (placedAny) break;
            }

            if (!placedAny)
            {
                passesWithoutPlacement++;
                if (passesWithoutPlacement >= maxPassesWithoutPlacement) break;
            }
            else
            {
                passesWithoutPlacement = 0;
            }
        }
    }
}
