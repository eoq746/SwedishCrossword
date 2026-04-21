namespace SwedishCrossword.Models;

/// <summary>
/// Represents a word in the crossword with its clue and placement information.
/// A word can be straight (single direction) or bent (vinkelord) with multiple segments.
/// </summary>
public class Word
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Text { get; init; } = string.Empty;
    public string Clue { get; init; } = string.Empty;
    public List<string> AlternativeClues { get; init; } = [];
    public string Category { get; init; } = string.Empty;
    public DifficultyLevel Difficulty { get; init; } = DifficultyLevel.Medium;

    // Placement information
    public int StartRow { get; set; } = -1;
    public int StartColumn { get; set; } = -1;
    public Direction Direction { get; set; } = Direction.Across;
    public int Number { get; set; } = 0;
    public bool IsPlaced { get; set; } = false;

    /// <summary>
    /// Segments defining the word's path on the grid.
    /// Empty list means a straight word using StartRow/StartColumn/Direction.
    /// For a vinkelord, contains 2+ segments where adjacent segments share a bend cell.
    /// Effective character count: sum(segment.Length) - BendCount
    /// </summary>
    public List<WordSegment> Segments { get; set; } = [];

    /// <summary>Whether this word bends (has multiple segments)</summary>
    public bool IsBent => Segments.Count > 1;

    /// <summary>Number of bends in the word (0 for straight words)</summary>
    public int BendCount => Math.Max(0, Segments.Count - 1);

    public int Length => Text.Length;

    public int EndRow => Segments.Count > 1
        ? Segments[^1].EndRow
        : Direction == Direction.Across ? StartRow : StartRow + Length - 1;

    public int EndColumn => Segments.Count > 1
        ? Segments[^1].EndCol
        : Direction == Direction.Across ? StartColumn + Length - 1 : StartColumn;

    public Word(string text, string clue, string category = "", DifficultyLevel difficulty = DifficultyLevel.Medium, List<string>? alternativeClues = null)
    {
        Text = text.ToUpper().Trim();
        Clue = clue.Trim();
        Category = category;
        Difficulty = difficulty;
        AlternativeClues = alternativeClues ?? [];
    }

    /// <summary>
    /// Returns a randomly selected clue from the primary clue and any alternatives.
    /// </summary>
    public string GetRandomClue()
    {
        if (AlternativeClues.Count == 0)
            return Clue;

        var allClues = new List<string>(AlternativeClues.Count + 1) { Clue };
        allClues.AddRange(AlternativeClues);
        return allClues[Random.Shared.Next(allClues.Count)];
    }

    /// <summary>
    /// Gets the character at the specified position within the word
    /// </summary>
    public char GetCharAt(int position)
    {
        if (position < 0 || position >= Text.Length)
            throw new ArgumentOutOfRangeException(nameof(position));

        return Text[position];
    }

    /// <summary>
    /// Gets all positions this word occupies in the grid.
    /// For bent words, walks each segment in order, skipping the first cell of
    /// subsequent segments (since it's the same as the last cell of the previous segment).
    /// </summary>
    public IEnumerable<(int Row, int Column)> GetPositions()
    {
        if (!IsPlaced) yield break;

        if (Segments.Count > 1)
        {
            // Bent word: walk segments, deduplicating shared bend cells
            for (int segIdx = 0; segIdx < Segments.Count; segIdx++)
            {
                var segment = Segments[segIdx];
                var positions = segment.GetPositions().ToList();

                // Skip the first position of subsequent segments (shared with previous segment's last cell)
                int start = segIdx == 0 ? 0 : 1;
                for (int i = start; i < positions.Count; i++)
                {
                    yield return positions[i];
                }
            }
        }
        else
        {
            // Straight word: original linear behavior
            for (int i = 0; i < Length; i++)
            {
                if (Direction == Direction.Across)
                    yield return (StartRow, StartColumn + i);
                else
                    yield return (StartRow + i, StartColumn);
            }
        }
    }

    /// <summary>
    /// Gets the direction at a specific character index within the word.
    /// For straight words, always returns the word's Direction.
    /// For bent words, returns the direction of the segment containing that index.
    /// </summary>
    public Direction GetDirectionAtIndex(int charIndex)
    {
        if (Segments.Count <= 1)
            return Direction;

        int offset = 0;
        for (int segIdx = 0; segIdx < Segments.Count; segIdx++)
        {
            var segment = Segments[segIdx];
            // Characters contributed by this segment (subtract 1 for shared bend cell, except first segment)
            int charsInSegment = segIdx == 0 ? segment.Length : segment.Length - 1;

            if (charIndex < offset + charsInSegment)
                return segment.Direction;

            offset += charsInSegment;
        }

        // Fallback: last segment's direction
        return Segments[^1].Direction;
    }

    /// <summary>
    /// Checks if this word intersects with another word at any point
    /// </summary>
    public bool IntersectsWith(Word other)
    {
        if (!IsPlaced || !other.IsPlaced) return false;

        var myPositions = GetPositions().ToHashSet();
        var otherPositions = other.GetPositions().ToHashSet();

        return myPositions.Intersect(otherPositions).Any();
    }

    /// <summary>
    /// Gets intersection points with another word.
    /// For bent words, intersections are allowed regardless of direction
    /// since different segments may have different directions.
    /// </summary>
    public IEnumerable<(int Row, int Column, int MyIndex, int OtherIndex)> GetIntersections(Word other)
    {
        if (!IsPlaced || !other.IsPlaced)
            yield break;

        // For straight words with the same direction, no intersections possible
        if (!IsBent && !other.IsBent && Direction == other.Direction)
            yield break;

        var myPositions = GetPositions().ToList();
        var otherPositions = other.GetPositions().ToList();

        for (int myIdx = 0; myIdx < myPositions.Count; myIdx++)
        {
            for (int otherIdx = 0; otherIdx < otherPositions.Count; otherIdx++)
            {
                if (myPositions[myIdx] == otherPositions[otherIdx])
                {
                    yield return (myPositions[myIdx].Row, myPositions[myIdx].Column, myIdx, otherIdx);
                }
            }
        }
    }

    public override string ToString()
    {
        var dirStr = IsBent ? "Vinkelord" : Direction.ToString();
        return $"{Number}. {Text} ({dirStr}) - {Clue}";
    }
}

public enum Direction
{
    Across,
    Down
}

public enum DifficultyLevel
{
    Easy,
    Medium,
    Hard
}