namespace SwedishCrossword.Services.Generation;

using SwedishCrossword.Models;

/// <summary>
/// Shared utility methods used across generation helper classes.
/// </summary>
internal static class GenerationHelpers
{
    public static int CountVowels(string text)
    {
        int count = 0;
        foreach (var c in text)
        {
            if (c is 'A' or 'E' or 'I' or 'O' or 'U' or 'Å' or 'Ä' or 'Ö')
                count++;
        }
        return count;
    }

    /// <summary>
    /// Finds words that match a pattern with some letters fixed and some as wildcards
    /// </summary>
    public static List<Word> FindWordsMatchingPattern(List<Word> candidateWords, List<char?> pattern,
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
                var p = pattern[i];
                if (p.HasValue && word.Text[i] != p.Value)
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

    /// <summary>
    /// Partially shuffles a list by randomizing among the top 'topRange' elements at each position.
    /// </summary>
    public static void ShuffleTopBiased<T>(List<T> list, int topRange, Random random)
    {
        var shuffleLimit = Math.Max(0, list.Count - topRange);
        for (int i = 0; i < shuffleLimit; i++)
        {
            int range = Math.Min(topRange, list.Count - i);
            int j = i + random.Next(range);
            if (i != j)
            {
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }

    public static Direction GetPreferredDirection(CrosswordGrid grid)
    {
        int acrossCount = 0;
        int downCount = 0;
        foreach (var w in grid.Words)
        {
            if (w.Direction == Direction.Across) acrossCount++;
            else downCount++;
        }

        return acrossCount <= downCount ? Direction.Across : Direction.Down;
    }

    public static int CountNearbyWords(CrosswordGrid grid, int centerRow, int centerCol, int radius)
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
}
