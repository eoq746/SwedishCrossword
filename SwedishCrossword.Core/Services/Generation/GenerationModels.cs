namespace SwedishCrossword.Services.Generation;

using SwedishCrossword.Models;

internal class WordAnalysis
{
    public Word Word { get; set; } = null!;
    public double ConnectivityScore { get; set; }
    public int VowelCount { get; set; }
    public int CommonLetterCount { get; set; }
}

internal class ScoredIntersection
{
    public (int Row, int Column, Direction Direction, Word IntersectingWord, int MyIndex, int TheirIndex) Intersection { get; set; }
    public double Score { get; set; }
}

internal class VerticalBridgeOpportunity
{
    public int Col { get; set; }
    public int StartRow { get; set; }
    public int Length { get; set; }
    public List<char?> Pattern { get; set; } = [];
    public int ExistingLetterCount { get; set; }
    public int EmptyCellCount { get; set; }
    public bool IsHorizontal { get; set; }

    /// <summary>
    /// Number of empty cells that have perpendicular adjacent letters.
    /// Each such cell will create an accidental word during validation.
    /// Lower = more likely to pass validation.
    /// </summary>
    public int PerpendicularConflicts { get; set; }
}

internal class VinkelordOpportunity
{
    public List<WordSegment> Segments { get; set; } = [];
    public List<char?> Pattern { get; set; } = [];
    public int TotalLength { get; set; }
    public int ExistingLetterCount { get; set; }
    public int EmptyCellCount { get; set; }
}
