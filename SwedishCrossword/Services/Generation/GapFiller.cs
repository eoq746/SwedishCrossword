namespace SwedishCrossword.Services.Generation;

using SwedishCrossword.Models;
using static GenerationHelpers;

/// <summary>
/// Handles gap filling and bridge filling strategies.
/// Scans rows and columns for patterns containing existing letters with empty
/// cells between them, then finds dictionary words that match those patterns.
/// </summary>
internal class GapFiller(SwedishDictionary dictionary, Random random)
{
    private readonly SwedishDictionary _dictionary = dictionary;
    private readonly Random _random = random;

    /// <summary>
    /// Finds bridge opportunities by generating sub-window patterns within each
    /// row and column. The previous implementation only split patterns at blocked
    /// cells or grid edges, which produced patterns spanning the entire dimension
    /// (e.g. length 17 on a 17×17 grid) — far too long to match any dictionary
    /// word. This version generates all valid sub-windows of length [minLength,
    /// maxLength] where CheckWordIsolation will pass (no letter immediately
    /// before or after the placed word).
    /// </summary>
    public List<VerticalBridgeOpportunity> FindVerticalBridgeOpportunities(CrosswordGrid grid, int minLength = 2, int maxLength = 8)
    {
        var opportunities = new List<VerticalBridgeOpportunity>();

        // Vertical bridges (scan columns)
        for (int col = 0; col < grid.Width; col++)
        {
            // Precompute cell state for this column
            var cellHasLetter = new bool[grid.Height];
            var cellLetters = new char?[grid.Height];
            var cellIsBlocked = new bool[grid.Height];
            for (int row = 0; row < grid.Height; row++)
            {
                var cell = grid.GetCell(row, col);
                cellIsBlocked[row] = cell.IsBlocked;
                if (cell.HasLetter)
                {
                    cellHasLetter[row] = true;
                    cellLetters[row] = cell.Letter;
                }
            }

            // Enumerate valid start rows: the cell before must not have a letter
            // (required for CheckWordIsolation to pass)
            for (int startRow = 0; startRow < grid.Height; startRow++)
            {
                if (startRow > 0 && cellHasLetter[startRow - 1])
                    continue;

                // Incrementally build pattern for increasing lengths
                var pattern = new List<char?>();
                int letterCount = 0;
                int emptyCount = 0;
                int perpConflicts = 0;

                for (int length = 1; startRow + length <= grid.Height; length++)
                {
                    int row = startRow + length - 1;

                    // Blocked cell — can't extend further from this start
                    if (cellIsBlocked[row])
                        break;

                    if (cellHasLetter[row])
                    {
                        pattern.Add(cellLetters[row]);
                        letterCount++;
                    }
                    else
                    {
                        pattern.Add(null);
                        emptyCount++;
                        // Check if this empty cell has perpendicular (horizontal) adjacent letters
                        if ((col > 0 && grid.GetCell(row, col - 1).HasLetter) ||
                            (col + 1 < grid.Width && grid.GetCell(row, col + 1).HasLetter))
                        {
                            perpConflicts++;
                        }
                    }

                    if (length < minLength) continue;
                    if (length > maxLength) break;

                    // Check valid end: the cell after must not have a letter
                    if (row + 1 < grid.Height && cellHasLetter[row + 1])
                        continue; // Can't end here, but might extend past this letter

                    if (letterCount >= 1 && emptyCount >= 1)
                    {
                        opportunities.Add(new VerticalBridgeOpportunity
                        {
                            Col = col,
                            StartRow = startRow,
                            Length = length,
                            Pattern = new List<char?>(pattern),
                            ExistingLetterCount = letterCount,
                            EmptyCellCount = emptyCount,
                            PerpendicularConflicts = perpConflicts
                        });
                    }
                }
            }
        }

        // Horizontal bridges (scan rows)
        for (int row = 0; row < grid.Height; row++)
        {
            var cellHasLetter = new bool[grid.Width];
            var cellLetters = new char?[grid.Width];
            var cellIsBlocked = new bool[grid.Width];
            for (int col = 0; col < grid.Width; col++)
            {
                var cell = grid.GetCell(row, col);
                cellIsBlocked[col] = cell.IsBlocked;
                if (cell.HasLetter)
                {
                    cellHasLetter[col] = true;
                    cellLetters[col] = cell.Letter;
                }
            }

            for (int startCol = 0; startCol < grid.Width; startCol++)
            {
                if (startCol > 0 && cellHasLetter[startCol - 1])
                    continue;

                var pattern = new List<char?>();
                int letterCount = 0;
                int emptyCount = 0;
                int perpConflicts = 0;

                for (int length = 1; startCol + length <= grid.Width; length++)
                {
                    int col = startCol + length - 1;

                    if (cellIsBlocked[col])
                        break;

                    if (cellHasLetter[col])
                    {
                        pattern.Add(cellLetters[col]);
                        letterCount++;
                    }
                    else
                    {
                        pattern.Add(null);
                        emptyCount++;
                        // Check if this empty cell has perpendicular (vertical) adjacent letters
                        if ((row > 0 && grid.GetCell(row - 1, col).HasLetter) ||
                            (row + 1 < grid.Height && grid.GetCell(row + 1, col).HasLetter))
                        {
                            perpConflicts++;
                        }
                    }

                    if (length < minLength) continue;
                    if (length > maxLength) break;

                    if (col + 1 < grid.Width && cellHasLetter[col + 1])
                        continue;

                    if (letterCount >= 1 && emptyCount >= 1)
                    {
                        opportunities.Add(new VerticalBridgeOpportunity
                        {
                            Col = startCol,
                            StartRow = row,
                            Length = length,
                            Pattern = new List<char?>(pattern),
                            ExistingLetterCount = letterCount,
                            EmptyCellCount = emptyCount,
                            IsHorizontal = true,
                            PerpendicularConflicts = perpConflicts
                        });
                    }
                }
            }
        }

        opportunities.Sort((a, b) =>
        {
            // Primary: fewer perpendicular conflicts = much more likely to pass validation
            int cmp = a.PerpendicularConflicts.CompareTo(b.PerpendicularConflicts);
            if (cmp != 0) return cmp;
            // Secondary: fewer empty cells = less validation risk
            cmp = a.EmptyCellCount.CompareTo(b.EmptyCellCount);
            if (cmp != 0) return cmp;
            // Tertiary: more existing letters = fills more cells with one placement
            return b.ExistingLetterCount.CompareTo(a.ExistingLetterCount);
        });
        return opportunities;
    }

    public async Task FillBridgeOpportunitiesAsync(CrosswordGrid grid, List<Word> candidateWords, HashSet<Word> placedWords,
        CrosswordGenerationOptions options, Dictionary<string, double> placedWordScores,
        Dictionary<string, double> connectivityScores, int maxBridgeLength, CancellationToken cancellationToken)
    {
        var usedWordTexts = grid.GetPlacedWordTexts();
        var placedWordTexts = new HashSet<string>(placedWords.Select(w => w.Text), StringComparer.OrdinalIgnoreCase);

        int yieldCounter = 0;
        int totalProcessed = 0;
        const int maxOpportunitiesPerPass = 300;

        while (totalProcessed < maxOpportunitiesPerPass && !cancellationToken.IsCancellationRequested)
        {
            var opportunities = FindVerticalBridgeOpportunities(grid, minLength: 2, maxLength: maxBridgeLength);
            if (opportunities.Count == 0) break;

            // Shuffle among top opportunities for variety across cycles
            ShuffleTopBiased(opportunities, 5, _random);

            bool placedAnyThisScan = false;
            var limit = Math.Min(maxOpportunitiesPerPass - totalProcessed, opportunities.Count);

            for (int oppIdx = 0; oppIdx < limit; oppIdx++)
            {
                if (cancellationToken.IsCancellationRequested) break;
                totalProcessed++;

                var opportunity = opportunities[oppIdx];

                var matchingWords = FindWordsMatchingPattern(candidateWords, opportunity.Pattern, placedWordTexts, usedWordTexts);
                if (matchingWords.Count == 0) continue;

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
                if (scored.Count > 25)
                    scored.RemoveRange(25, scored.Count - 25);
                ShuffleTopBiased(scored, 5, _random);

                var tryCount = Math.Min(15, scored.Count);
                for (int i = 0; i < tryCount; i++)
                {
                    var word = scored[i].Word;
                    var direction = opportunity.IsHorizontal ? Direction.Across : Direction.Down;
                    var row = opportunity.StartRow;
                    var col = opportunity.Col;

                    if (grid.TryPlaceWordWithValidation(word, row, col, direction, _dictionary, options.RejectInvalidWords, options.RejectDuplicateWords))
                    {
                        placedWords.Add(word);
                        placedWordTexts.Add(word.Text);
                        usedWordTexts.Add(word.Text);
                        placedWordScores[word.Text] = connectivityScores.GetValueOrDefault(word.Text);
                        placedAnyThisScan = true;
                        break;
                    }
                }

                if (++yieldCounter % 10 == 0)
                    await Task.Yield();
            }

            if (!placedAnyThisScan) break;
        }
    }
}
