namespace SwedishCrossword.Models;

/// <summary>
/// Represents a single cell in the crossword grid
/// </summary>
public class GridCell
{
    public char Letter { get; set; } = '\0';
    public bool IsBlocked { get; set; } = false;
    public bool IsPartOfWord { get; set; } = false;
    public int Number { get; set; } = 0; // For numbering clues
    public HashSet<string> WordIds { get; set; } = [];

    public bool IsEmpty => Letter == '\0' && !IsBlocked;
    public bool HasLetter => Letter != '\0' && Letter != '*'; // Don't consider asterisks as letters
    public bool HasAsterisk => Letter == '*';
    public bool IsNumbered => Number > 0;

    public void SetLetter(char letter, string wordId)
    {
        Letter = char.ToUpper(letter);
        IsPartOfWord = true;
        WordIds.Add(wordId);
    }

    public void Block()
    {
        IsBlocked = true;
        Letter = '\0';
        IsPartOfWord = false;
        Number = 0;
        WordIds.Clear();
    }

    public void Clear()
    {
        Letter = '\0';
        IsBlocked = false;
        IsPartOfWord = false;
        Number = 0;
        WordIds.Clear();
    }

    public override string ToString()
    {
        if (IsBlocked) return "#";  // Hash instead of problematic Unicode character
        if (HasLetter) return Letter.ToString();
        if (HasAsterisk) return "*"; // Show asterisk for filled empty cells
        return " "; // Original behavior for truly empty cells
    }
}