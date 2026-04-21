using System.Globalization;
using System.Text;
using SwedishCrossword.Services;

namespace SwedishCrossword.Models;

/// <summary>
/// Represents an accidental word found in the crossword
/// </summary>
public class AccidentalWord
{
    public string Text { get; set; } = string.Empty;
    public int StartRow { get; set; }
    public int StartCol { get; set; }
    public Direction Direction { get; set; }
    public int Length { get; set; }
    public bool? IsValidSwedishWord { get; set; }
    public bool ShouldIncludeInPuzzle { get; set; } // NEW: Mark if should be included as puzzle clue
    public int PuzzleNumber { get; set; } // NEW: Number for the puzzle if included
    public string ClueFromDictionary { get; set; } = string.Empty; // NEW: Clue from dictionary

    public string ValidationStatus =>
        IsValidSwedishWord switch
        {
            null => "? Ej kontrollerat",
            true when ShouldIncludeInPuzzle => "? Giltigt ord (inkluderat i pussel)",
            true => "? Giltigt svenskt ord",
            false => "? Ogiltigt ord"
        };

    public override string ToString()
    {
        var direction = Direction == Direction.Across ? "vågrätt" : "lodrätt";
        var position = $"({StartRow + 1}, {StartCol + 1})"; // 1-based for display
        var number = ShouldIncludeInPuzzle ? $" #{PuzzleNumber}" : "";
        return $"{Text}{number} - {direction} från {position} - {ValidationStatus}";
    }
}

/// <summary>
/// Results from crossword validation
/// </summary>
public class CrosswordValidationResult
{
    public bool IsValid { get; set; } = true;
    public List<AccidentalWord> AccidentalWords { get; set; } = [];
    public List<AccidentalWord> ValidAccidentalWords { get; set; } = [];
    public List<AccidentalWord> InvalidAccidentalWords { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];

    public bool HasInvalidWords => InvalidAccidentalWords.Count > 0;

    public string GetSummary()
    {
        var parts = new List<string>();

        if (AccidentalWords.Count > 0)
        {
            parts.Add($"Totalt: {AccidentalWords.Count}");
            parts.Add($"Giltiga svenska ord: {ValidAccidentalWords.Count}");
            parts.Add($"Ogiltiga ord: {InvalidAccidentalWords.Count}");
        }
        else
        {
            parts.Add("Inga oavsiktliga ord hittades");
        }

        return string.Join(", ", parts);
    }

    public string GetDetailedReport()
    {
        var sb = new StringBuilder();

        if (ValidAccidentalWords.Count > 0)
        {
            sb.AppendLine("Giltiga oavsiktliga ord:");
            foreach (var word in ValidAccidentalWords)
            {
                var direction = word.Direction == Direction.Across ? "vågrätt" : "lodrätt";
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {word.Text} - {direction} från ({word.StartRow + 1}, {word.StartCol + 1})");
            }
        }

        if (InvalidAccidentalWords.Count > 0)
        {
            sb.AppendLine("Ogiltiga ord som bör åtgärdas:");
            foreach (var word in InvalidAccidentalWords)
            {
                var direction = word.Direction == Direction.Across ? "vågrätt" : "lodrätt";
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {word.Text} - {direction} från ({word.StartRow + 1}, {word.StartCol + 1})");
            }
        }

        return sb.ToString();
    }
}

/// <summary>
/// Statistics about the crossword grid
/// </summary>
public class GridStats
{
    public int TotalCells { get; set; }
    public int FilledCells { get; set; }
    public int BlockedCells { get; set; }
    public int EmptyCells { get; set; }
    public int WordCount { get; set; }
    public double FillPercentage { get; set; }
    public int VinkelOrd { get; set; }
}
