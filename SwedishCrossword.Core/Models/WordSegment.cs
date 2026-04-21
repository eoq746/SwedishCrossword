namespace SwedishCrossword.Models;

/// <summary>
/// Represents one straight segment of a word's path on the grid.
/// A straight word has one segment. A vinkelord (bent word) has two or more segments.
/// Adjacent segments share their boundary cell (the bend point): the last cell of segment[i]
/// occupies the same grid position as the first cell of segment[i+1].
/// </summary>
public class WordSegment
{
    /// <summary>Row of the first cell in this segment</summary>
    public int StartRow { get; set; }

    /// <summary>Column of the first cell in this segment</summary>
    public int StartCol { get; set; }

    /// <summary>Direction this segment travels (Across or Down)</summary>
    public Direction Direction { get; set; }

    /// <summary>
    /// Number of characters in this segment, including the shared bend cell.
    /// Minimum value is 2 (a segment of length 1 would mean the bend cell has no run).
    /// </summary>
    public int Length { get; set; }

    /// <summary>Row of the last cell in this segment</summary>
    public int EndRow => Direction == Direction.Across ? StartRow : StartRow + Length - 1;

    /// <summary>Column of the last cell in this segment</summary>
    public int EndCol => Direction == Direction.Across ? StartCol + Length - 1 : StartCol;

    /// <summary>
    /// Enumerates all (Row, Column) positions this segment occupies.
    /// </summary>
    public IEnumerable<(int Row, int Column)> GetPositions()
    {
        for (int i = 0; i < Length; i++)
        {
            if (Direction == Direction.Across)
                yield return (StartRow, StartCol + i);
            else
                yield return (StartRow + i, StartCol);
        }
    }
}
