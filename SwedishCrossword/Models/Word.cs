namespace SwedishCrossword.Models;

/// <summary>
/// Represents a word in the crossword with its clue and placement information
/// </summary>
public class Word
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Text { get; init; } = string.Empty;
    public string Clue { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public DifficultyLevel Difficulty { get; init; } = DifficultyLevel.Medium;

    // Placement information
    public int StartRow { get; set; } = -1;
    public int StartColumn { get; set; } = -1;
    public Direction Direction { get; set; } = Direction.Across;
    public int Number { get; set; } = 0;
    public bool IsPlaced { get; set; } = false;

    public int Length => Text.Length;
    public int EndRow => Direction == Direction.Across ? StartRow : StartRow + Length - 1;
    public int EndColumn => Direction == Direction.Across ? StartColumn + Length - 1 : StartColumn;

    public Word(string text, string clue, string category = "", DifficultyLevel difficulty = DifficultyLevel.Medium)
    {
        Text = text.ToUpper().Trim();
        Clue = clue.Trim();
        Category = category;
        Difficulty = difficulty;
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
    /// Gets all positions this word occupies in the grid
    /// </summary>
    public IEnumerable<(int Row, int Column)> GetPositions()
    {
        if (!IsPlaced) yield break;

        for (int i = 0; i < Length; i++)
        {
            if (Direction == Direction.Across)
                yield return (StartRow, StartColumn + i);
            else
                yield return (StartRow + i, StartColumn);
        }
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
    /// Gets intersection points with another word
    /// </summary>
    public IEnumerable<(int Row, int Column, int MyIndex, int OtherIndex)> GetIntersections(Word other)
    {
        if (!IsPlaced || !other.IsPlaced || Direction == other.Direction)
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
        return $"{Number}. {Text} ({Direction}) - {Clue}";
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