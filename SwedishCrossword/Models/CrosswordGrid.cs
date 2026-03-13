using System.Text;
using SwedishCrossword.Services;

namespace SwedishCrossword.Models;

/// <summary>
/// Represents the main crossword grid with all its words and cells.
/// 
/// VALIDATION APPROACH:
/// This class supports two placement strategies:
/// 1. Standard placement (TryPlaceWord) - places words without validation
/// 2. Validation-aware placement (TryPlaceWordWithValidation) - validates accidental words during placement
/// 
/// The validation-aware approach prevents invalid crosswords by checking for invalid accidental words
/// as each word is placed, rejecting placements that would create invalid letter combinations.
/// </summary>
public class CrosswordGrid
{
    private readonly GridCell[,] _cells;
    private readonly List<Word> _words = [];

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<Word> Words => _words.AsReadOnly();

    public CrosswordGrid(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException("Grid dimensions must be positive");

        Width = width;
        Height = height;
        _cells = new GridCell[height, width];

        // Initialize all cells
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                _cells[row, col] = new GridCell();
            }
        }
    }

    public GridCell GetCell(int row, int column)
    {
        if (!IsValidPosition(row, column))
            throw new ArgumentOutOfRangeException($"Position ({row}, {column}) is outside grid bounds");
        
        return _cells[row, column];
    }

    public bool IsValidPosition(int row, int column)
    {
        return row >= 0 && row < Height && column >= 0 && column < Width;
    }

    /// <summary>
    /// Attempts to place a word on the grid with validation to prevent invalid accidental words
    /// </summary>
    public bool TryPlaceWordWithValidation(Word word, int startRow, int startCol, Direction direction, Services.SwedishDictionary? dictionary = null, bool rejectInvalidWords = true)
    {
        if (!CanPlaceWord(word, startRow, startCol, direction))
            return false;

        // DUPLICATE CHECK: Reject if this exact word text is already placed
        var wordTextUpper = word.Text.ToUpperInvariant();
        foreach (var existingWord in _words)
        {
            if (existingWord.Text.Equals(wordTextUpper, StringComparison.OrdinalIgnoreCase))
                return false; // This word text is already in the puzzle
        }

        // CONNECTIVITY CHECK: If this is not the first word, ensure it connects to existing words
        if (_words.Count > 0 && !WouldConnectToExistingWords(word, startRow, startCol, direction))
        {
            return false; // Reject placement if it would create an isolated word
        }

        // Create a comprehensive backup to test the placement
        var originalState = CreateGridBackup();
        
        try
        {
            // Temporarily place the word to test validation
            word.StartRow = startRow;
            word.StartColumn = startCol;
            word.Direction = direction;
            word.IsPlaced = true;
            
            // Place letters on grid temporarily
            for (int i = 0; i < word.Length; i++)
            {
                int row = direction == Direction.Across ? startRow : startRow + i;
                int col = direction == Direction.Across ? startCol + i : startCol;
                
                var cell = GetCell(row, col);
                cell.SetLetter(word.GetCharAt(i), word.Id);
            }

            _words.Add(word);

            // Validate if dictionary checking is enabled
            bool isValid = true;
            if (dictionary != null && rejectInvalidWords)
            {
                // Use enhanced detection that checks all potentially affected areas
                var accidentalWords = DetectAccidentalWordsNear(startRow, startCol, direction, word.Length, dictionary);
                
                // Check for invalid accidental words
                foreach (var accWord in accidentalWords)
                {
                    if (accWord.IsValidSwedishWord == false)
                    {
                        isValid = false;
                        break;
                    }
                }
                
                // Also check for duplicate accidental words if still valid
                if (isValid)
                {
                    // Build HashSet of existing word texts (excluding the word we just placed)
                    var existingWordTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var w in _words)
                    {
                        if (w.Id != word.Id)
                            existingWordTexts.Add(w.Text);
                    }
                    
                    // Check if any valid accidental word duplicates an existing word
                    foreach (var accWord in accidentalWords)
                    {
                        if (accWord.IsValidSwedishWord == true && existingWordTexts.Contains(accWord.Text))
                        {
                            isValid = false;
                            break;
                        }
                    }
                }
            }

            if (isValid)
            {
                // Placement is valid - renumber clues and return success
                RenumberCluesIncludingAccidental(null);
                return true;
            }
            else
            {
                // Rollback the placement using comprehensive restore
                RestoreGridFromBackup(originalState);
                return false;
            }
        }
        catch (Exception ex)
        {
            // On any error, ensure we restore the grid state
            Console.WriteLine($"    Fel under ordvalidering för '{word.Text}': {ex.Message}");
            RestoreGridFromBackup(originalState);
            return false;
        }
    }

    /// <summary>
    /// Creates a comprehensive backup of the current grid state
    /// </summary>
    private GridBackup CreateGridBackup()
    {
        var backup = new GridBackup();
        
        // Back up all cell states
        for (int row = 0; row < Height; row++)
        {
            for (int col = 0; col < Width; col++)
            {
                var cell = GetCell(row, col);
                backup.CellStates[(row, col)] = new CellBackup
                {
                    Letter = cell.Letter,
                    Number = cell.Number,
                    WordIds = new HashSet<string>(cell.WordIds),
                    IsPartOfWord = cell.IsPartOfWord,
                    BendArrowDirection = cell.BendArrowDirection
                };
            }
        }
        
        // Back up words list
        backup.WordsList = new List<Word>(_words);
        
        return backup;
    }

    /// <summary>
    /// Restores the grid from a comprehensive backup
    /// </summary>
    private void RestoreGridFromBackup(GridBackup backup)
    {
        // Restore all cell states
        foreach (var kvp in backup.CellStates)
        {
            var (row, col) = kvp.Key;
            var cellBackup = kvp.Value;
            var cell = GetCell(row, col);
            
            cell.WordIds.Clear();
            cell.WordIds.UnionWith(cellBackup.WordIds);
            cell.Letter = cellBackup.Letter;
            cell.Number = cellBackup.Number;
            cell.IsPartOfWord = cellBackup.IsPartOfWord;
            cell.BendArrowDirection = cellBackup.BendArrowDirection;
        }
        
        // Restore words list and reset any word that was being placed
        _words.Clear();
        _words.AddRange(backup.WordsList);
        
        // Reset any word states that might have been modified
        foreach (var word in _words)
        {
            word.IsPlaced = true; // All words in backup were already placed
        }
    }

    /// <summary>
    /// Helper class for comprehensive grid state backup
    /// </summary>
    private class GridBackup
    {
        public Dictionary<(int Row, int Col), CellBackup> CellStates { get; } = new();
        public List<Word> WordsList { get; set; } = new();
    }

    /// <summary>
    /// Helper class for cell state backup
    /// </summary>
    private class CellBackup
    {
        public char Letter { get; set; }
        public int Number { get; set; }
        public HashSet<string> WordIds { get; set; } = new();
        public bool IsPartOfWord { get; set; }
        public Direction? BendArrowDirection { get; set; }
    }

    /// <summary>
    /// Attempts to place a word on the grid
    /// </summary>
    public bool TryPlaceWord(Word word, int startRow, int startCol, Direction direction)
    {
        if (!CanPlaceWord(word, startRow, startCol, direction))
            return false;

        return PlaceWord(word, startRow, startCol, direction);
    }

    /// <summary>
    /// Checks if a word can be placed at the specified position
    /// </summary>
    public bool CanPlaceWord(Word word, int startRow, int startCol, Direction direction)
    {
        // Check bounds
        int endRow = direction == Direction.Across ? startRow : startRow + word.Length - 1;
        int endCol = direction == Direction.Across ? startCol + word.Length - 1 : startCol;

        if (!IsValidPosition(startRow, startCol) || !IsValidPosition(endRow, endCol))
            return false;

        // Check each position the word would occupy
        for (int i = 0; i < word.Length; i++)
        {
            int row = direction == Direction.Across ? startRow : startRow + i;
            int col = direction == Direction.Across ? startCol + i : startCol;
            
            var cell = GetCell(row, col);
            
            // Cell must be empty or contain the same letter
            if (cell.IsBlocked)
                return false;
            
            if (cell.HasLetter && cell.Letter != word.GetCharAt(i))
                return false;
        }

        // Check for word isolation (no adjacent words except at intersections)
        return CheckWordIsolation(word, startRow, startCol, direction);
    }

    private bool CheckWordIsolation(Word word, int startRow, int startCol, Direction direction)
    {
        // Check positions before and after the word
        if (direction == Direction.Across)
        {
            // Check left of word
            if (startCol > 0 && GetCell(startRow, startCol - 1).HasLetter)
                return false;

            // Check right of word
            if (startCol + word.Length < Width && GetCell(startRow, startCol + word.Length).HasLetter)
                return false;
        }
        else
        {
            // Check above word
            if (startRow > 0 && GetCell(startRow - 1, startCol).HasLetter)
                return false;

            // Check below word
            if (startRow + word.Length < Height && GetCell(startRow + word.Length, startCol).HasLetter)
                return false;
        }

        // Check that no new (empty) cell in this word lands immediately after the tail-end
        // of any existing word's endpoint in any direction, and that no cell lands
        // immediately before the first cell of any existing bent word's first segment
        // (which would create an accidental merged reading such as "AMENIG").
        for (int i = 0; i < word.Length; i++)
        {
            int cellRow = direction == Direction.Across ? startRow : startRow + i;
            int cellCol = direction == Direction.Across ? startCol + i : startCol;

            if (!GetCell(cellRow, cellCol).HasLetter && WouldFollowAnyWordEnd(cellRow, cellCol))
                return false;

            if (!GetCell(cellRow, cellCol).HasLetter && WouldPrecedeAnyBentWordStart(cellRow, cellCol))
                return false;
        }

        // The terminal cell must not already carry a BendArrowDirection from an existing
        // bent word. If it does, readers of this word would follow that arrow past its
        // actual end.
        int terminalRow = direction == Direction.Across ? startRow : startRow + word.Length - 1;
        int terminalCol = direction == Direction.Across ? startCol + word.Length - 1 : startCol;
        if (GetCell(terminalRow, terminalCol).BendArrowDirection != null)
            return false;

        return true;
    }

    /// <summary>
    /// Returns true if placing a new letter at (row, col) would land immediately after the
    /// endpoint of any existing word (straight or bent, in any direction), which would cause
    /// that word to end in a non-empty cell.
    /// </summary>
    private bool WouldFollowAnyWordEnd(int row, int col)
    {
        foreach (var w in _words)
        {
            if (!w.IsPlaced) continue;

            if (!w.IsBent)
            {
                if (w.Direction == Direction.Across &&
                    w.StartRow == row &&
                    w.StartColumn + w.Length == col)
                    return true;

                if (w.Direction == Direction.Down &&
                    w.StartColumn == col &&
                    w.StartRow + w.Length == row)
                    return true;
            }
            else
            {
                if (w.Segments.Count == 0) continue;
                var lastSeg = w.Segments[^1];

                if (lastSeg.Direction == Direction.Across &&
                    lastSeg.EndRow == row &&
                    lastSeg.EndCol + 1 == col)
                    return true;

                if (lastSeg.Direction == Direction.Down &&
                    lastSeg.EndCol == col &&
                    lastSeg.EndRow + 1 == row)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true if placing a new letter at (row, col) would land immediately before the
    /// first cell of any existing bent word's first segment (in that segment's reading direction).
    /// This prevents an accidental continuous reading that merges the new letter with the bent
    /// word's path (e.g. placing 'A' at the cell just before a bent word starting with 'M'
    /// would create the misleading reading "AMENIG").
    /// </summary>
    private bool WouldPrecedeAnyBentWordStart(int row, int col)
    {
        foreach (var w in _words)
        {
            if (!w.IsPlaced || !w.IsBent || w.Segments.Count == 0) continue;

            var firstSeg = w.Segments[0];

            if (firstSeg.Direction == Direction.Across &&
                firstSeg.StartRow == row &&
                firstSeg.StartCol == col + 1)
                return true;

            if (firstSeg.Direction == Direction.Down &&
                firstSeg.StartCol == col &&
                firstSeg.StartRow == row + 1)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Places a word on the grid
    /// </summary>
    private bool PlaceWord(Word word, int startRow, int startCol, Direction direction)
    {
        // Set word placement info
        word.StartRow = startRow;
        word.StartColumn = startCol;
        word.Direction = direction;
        word.IsPlaced = true;

        // Place letters on grid
        for (int i = 0; i < word.Length; i++)
        {
            int row = direction == Direction.Across ? startRow : startRow + i;
            int col = direction == Direction.Across ? startCol + i : startCol;
            
            var cell = GetCell(row, col);
            cell.SetLetter(word.GetCharAt(i), word.Id);
        }

        _words.Add(word);
        
        // Renumber all clues after placing a new word
        RenumberCluesIncludingAccidental(null);
        
        return true;
    }

    /// <summary>
    /// Removes a word from the grid
    /// </summary>
    public bool RemoveWord(Word word)
    {
        if (!word.IsPlaced || !_words.Contains(word))
            return false;

        // Clear cells that only belong to this word
        foreach (var (row, col) in word.GetPositions())
        {
            var cell = GetCell(row, col);
            cell.WordIds.Remove(word.Id);
            
            if (cell.WordIds.Count == 0)
            {
                cell.Clear();
            }
        }

        word.IsPlaced = false;
        word.Number = 0;
        _words.Remove(word);

        // Renumber all remaining words
        RenumberCluesIncludingAccidental(null);

        return true;
    }

    /// <summary>
    /// Gets all possible intersection points for a word with existing words
    /// </summary>
    public IEnumerable<(int Row, int Column, Direction Direction, Word IntersectingWord, int MyIndex, int TheirIndex)> GetPossibleIntersections(Word word)
    {
        foreach (var existingWord in _words)
        {
            if (existingWord.Direction == Direction.Across)
            {
                // Try placing word vertically intersecting with horizontal word
                for (int myIdx = 0; myIdx < word.Length; myIdx++)
                {
                    for (int theirIdx = 0; theirIdx < existingWord.Length; theirIdx++)
                    {
                        if (word.GetCharAt(myIdx) == existingWord.GetCharAt(theirIdx))
                        {
                            int row = existingWord.StartRow - myIdx;
                            int col = existingWord.StartColumn + theirIdx;
                            
                            if (row >= 0 && row < Height && col >= 0 && col < Width)
                            {
                                yield return (row, col, Direction.Down, existingWord, myIdx, theirIdx);
                            }
                        }
                    }
                }
            }
            else // existingWord is Down
            {
                // Try placing word horizontally intersecting with vertical word
                for (int myIdx = 0; myIdx < word.Length; myIdx++)
                {
                    for (int theirIdx = 0; theirIdx < existingWord.Length; theirIdx++)
                    {
                        if (word.GetCharAt(myIdx) == existingWord.GetCharAt(theirIdx))
                        {
                            int row = existingWord.StartRow + theirIdx;
                            int col = existingWord.StartColumn - myIdx;
                            
                            if (row >= 0 && row < Height && col >= 0 && col < Width)
                            {
                                yield return (row, col, Direction.Across, existingWord, myIdx, theirIdx);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Renumbers all clues based on grid position
    /// </summary>
    public void RenumberClues()
    {
        RenumberCluesIncludingAccidental(null);
    }

    /// <summary>
    /// Gets statistics about the grid
    /// </summary>
    public GridStats GetStats()
    {
        int filledCells = 0;
        int blockedCells = 0;
        
        for (int row = 0; row < Height; row++)
        {
            for (int col = 0; col < Width; col++)
            {
                var cell = GetCell(row, col);
                if (cell.HasLetter)
                    filledCells++;
                else if (cell.IsBlocked)
                    blockedCells++;
            }
        }

        int totalCells = Width * Height;
        int emptyCells = totalCells - filledCells - blockedCells;
        double fillPercentage = (double)filledCells / totalCells * 100;

        return new GridStats
        {
            TotalCells = totalCells,
            FilledCells = filledCells,
            BlockedCells = blockedCells,
            EmptyCells = emptyCells,
            WordCount = _words.Count,
            FillPercentage = fillPercentage,
            VinkelOrd = _words.Count(w => w.IsBent)
        };
    }

    /// <summary>
    /// Gets all placed word texts as a case-insensitive HashSet for duplicate checking
    /// </summary>
    public HashSet<string> GetPlacedWordTexts()
    {
        return _words.Select(w => w.Text).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets words organized by direction
    /// </summary>
    public (List<Word> Across, List<Word> Down) GetWordsByDirection()
    {
        var across = _words.Where(w => w.Direction == Direction.Across).ToList();
        var down = _words.Where(w => w.Direction == Direction.Down).ToList();
        return (across, down);
    }

    /// <summary>
    /// Converts grid to display string
    /// </summary>
    public string ToDisplayString(bool showNumbers = false, bool showSolution = true)
    {
        var sb = new StringBuilder();
        
        for (int row = 0; row < Height; row++)
        {
            for (int col = 0; col < Width; col++)
            {
                var cell = GetCell(row, col);
                
                if (cell.IsBlocked)
                {
                    sb.Append('#');
                }
                else if (showSolution && cell.HasLetter)
                {
                    if (showNumbers && cell.IsNumbered)
                    {
                        sb.Append($"{cell.Number}");
                    }
                    else
                    {
                        sb.Append(cell.Letter);
                    }
                }
                else if (showNumbers && cell.IsNumbered)
                {
                    sb.Append($"{cell.Number}");
                }
                else if (cell.HasAsterisk)
                {
                    sb.Append('*'); // Show asterisk for filled empty cells
                }
                else if (cell.IsEmpty)
                {
                    sb.Append('_'); // Show underscore for truly empty cells (before asterisks are added)
                }
                else
                {
                    sb.Append('_');
                }
                
                if (col < Width - 1)
                    sb.Append(' ');
            }
            sb.AppendLine();
        }
        
        return sb.ToString();
    }

    /// <summary>
    /// Detects accidental words formed in the grid
    /// </summary>
    public List<AccidentalWord> DetectAccidentalWords(Services.SwedishDictionary? dictionary = null)
    {
        var accidentalWords = new List<AccidentalWord>();
        var detectedWords = new HashSet<string>();

        // Check all cells for potential word starts
        for (int row = 0; row < Height; row++)
        {
            for (int col = 0; col < Width; col++)
            {
                // Try extracting horizontal word (only at actual word starts)
                var horizontalWord = ExtractHorizontalWord(row, col);
                if (horizontalWord != null && IsAccidentalWord(horizontalWord))
                {
                    var wordKey = $"{horizontalWord.Text}-{horizontalWord.StartRow}-{horizontalWord.StartCol}-{horizontalWord.Direction}";
                    if (!detectedWords.Contains(wordKey))
                    {
                        accidentalWords.Add(horizontalWord);
                        detectedWords.Add(wordKey);
                    }
                }

                // Try extracting vertical word (only at actual word starts)
                var verticalWord = ExtractVerticalWord(row, col);
                if (verticalWord != null && IsAccidentalWord(verticalWord))
                {
                    var wordKey = $"{verticalWord.Text}-{verticalWord.StartRow}-{verticalWord.StartCol}-{verticalWord.Direction}";
                    if (!detectedWords.Contains(wordKey))
                    {
                        accidentalWords.Add(verticalWord);
                        detectedWords.Add(wordKey);
                    }
                }
            }
        }

        // Second pass: detect straight words at bent word starting cells.
        // A bent word's start cell is a known word boundary, but a later-placed
        // perpendicular word may have added a letter to the adjacent cell (e.g.
        // the cell above for a Down-start bent word). ExtractVerticalWord then
        // treats the start cell as "mid-word" and skips it, so the short straight
        // word sharing the start cell (e.g. "BER" alongside bent "BETT") is never
        // extracted. Force-extracting from these positions fixes that.
        foreach (var bentWord in _words.Where(w => w.IsBent))
        {
            var startRow = bentWord.StartRow;
            var startCol = bentWord.StartColumn;
            var initialDir = bentWord.Direction;

            var sb = new StringBuilder();
            if (initialDir == Direction.Down)
            {
                int r = startRow;
                while (r < Height && GetCell(r, startCol).HasLetter)
                {
                    sb.Append(GetCell(r, startCol).Letter);
                    r++;
                }
            }
            else
            {
                int c = startCol;
                while (c < Width && GetCell(startRow, c).HasLetter)
                {
                    sb.Append(GetCell(startRow, c).Letter);
                    c++;
                }
            }

            var text = sb.ToString();
            if (text.Length >= 2)
            {
                var accWord = new AccidentalWord
                {
                    Text = text,
                    StartRow = startRow,
                    StartCol = startCol,
                    Direction = initialDir,
                    Length = text.Length
                };

                if (IsAccidentalWord(accWord))
                {
                    var wordKey = $"{accWord.Text}-{accWord.StartRow}-{accWord.StartCol}-{accWord.Direction}";
                    if (!detectedWords.Contains(wordKey))
                    {
                        accidentalWords.Add(accWord);
                        detectedWords.Add(wordKey);
                    }
                }
            }
        }

        // Validate against dictionary if provided
        if (dictionary != null)
        {
            foreach (var accWord in accidentalWords)
            {
                accWord.IsValidSwedishWord = dictionary.IsValidWord(accWord.Text);
                
                // If valid, get clue from dictionary and mark for inclusion
                if (accWord.IsValidSwedishWord == true)
                {
                    var dictionaryWords = dictionary.AllWords.Where(w => 
                        w.Text.Equals(accWord.Text, StringComparison.OrdinalIgnoreCase));
                    
                    if (dictionaryWords.Any())
                    {
                        var dictWord = dictionaryWords.First();
                        accWord.ClueFromDictionary = dictWord.Clue;
                        
                        // Check if this accidental word doesn't conflict with intentional words at same position
                        bool isAlreadyIntentional = Words.Any(w => 
                            w.StartRow == accWord.StartRow && 
                            w.StartColumn == accWord.StartCol && 
                            w.Direction == accWord.Direction &&
                            w.Text.Equals(accWord.Text, StringComparison.OrdinalIgnoreCase));

                        // Mark for inclusion if it's truly accidental (not intentionally placed at same position)
                        // Note: We no longer filter by word text duplication here because
                        // TryPlaceWordWithValidation now prevents duplicate word texts during placement
                        if (!isAlreadyIntentional)
                        {
                            accWord.ShouldIncludeInPuzzle = true;
                        }
                    }
                }
            }
        }

        return accidentalWords;
    }

    /// <summary>
    /// Optimized version that only checks near a newly placed word
    /// Enhanced to check all potentially affected accidental words
    /// </summary>
    public List<AccidentalWord> DetectAccidentalWordsNear(int startRow, int startCol, Direction direction, int length, Services.SwedishDictionary dictionary)
    {
        var accidentalWords = new List<AccidentalWord>();
        var detectedWords = new HashSet<string>();
        
        // For each cell the new word occupies, we need to check:
        // 1. The full horizontal word that passes through that cell
        // 2. The full vertical word that passes through that cell
        
        for (int i = 0; i < length; i++)
        {
            int cellRow = direction == Direction.Across ? startRow : startRow + i;
            int cellCol = direction == Direction.Across ? startCol + i : startCol;
            
            // Find the START of any horizontal word that includes this cell
            int horizStartCol = cellCol;
            while (horizStartCol > 0 && GetCell(cellRow, horizStartCol - 1).HasLetter)
            {
                horizStartCol--;
            }
            
            // Extract and validate the horizontal word starting from its actual start
            var horizontalWord = ExtractHorizontalWord(cellRow, horizStartCol);
            if (horizontalWord != null)
            {
                var wordKey = $"{horizontalWord.Text}-{horizontalWord.StartRow}-{horizontalWord.StartCol}-{horizontalWord.Direction}";
                if (!detectedWords.Contains(wordKey))
                {
                    // Check if this is an accidental word (not an intentionally placed word)
                    if (IsAccidentalWord(horizontalWord))
                    {
                        horizontalWord.IsValidSwedishWord = dictionary.IsValidWord(horizontalWord.Text);
                        accidentalWords.Add(horizontalWord);
                    }
                    detectedWords.Add(wordKey);
                }
            }
            
            // Find the START of any vertical word that includes this cell
            int vertStartRow = cellRow;
            while (vertStartRow > 0 && GetCell(vertStartRow - 1, cellCol).HasLetter)
            {
                vertStartRow--;
            }
            
            // Extract and validate the vertical word starting from its actual start
            var verticalWord = ExtractVerticalWord(vertStartRow, cellCol);
            if (verticalWord != null)
            {
                var wordKey = $"{verticalWord.Text}-{verticalWord.StartRow}-{verticalWord.StartCol}-{verticalWord.Direction}";
                if (!detectedWords.Contains(wordKey))
                {
                    // Check if this is an accidental word (not an intentionally placed word)
                    if (IsAccidentalWord(verticalWord))
                    {
                        verticalWord.IsValidSwedishWord = dictionary.IsValidWord(verticalWord.Text);
                        accidentalWords.Add(verticalWord);
                    }
                    detectedWords.Add(wordKey);
                }
            }
        }
        
        // Also check cells immediately before and after the word in its direction
        // These could form new words by extending existing sequences
        if (direction == Direction.Across)
        {
            // Check cell before word start
            if (startCol > 0)
            {
                int checkCol = startCol - 1;
                if (GetCell(startRow, checkCol).HasLetter)
                {
                    // Find start of horizontal word
                    int horizStart = checkCol;
                    while (horizStart > 0 && GetCell(startRow, horizStart - 1).HasLetter)
                    {
                        horizStart--;
                    }
                    var word = ExtractHorizontalWord(startRow, horizStart);
                    if (word != null && IsAccidentalWord(word))
                    {
                        var wordKey = $"{word.Text}-{word.StartRow}-{word.StartCol}-{word.Direction}";
                        if (!detectedWords.Contains(wordKey))
                        {
                            word.IsValidSwedishWord = dictionary.IsValidWord(word.Text);
                            accidentalWords.Add(word);
                            detectedWords.Add(wordKey);
                        }
                    }
                }
            }
            
            // Check cell after word end
            int endCol = startCol + length;
            if (endCol < Width && GetCell(startRow, endCol).HasLetter)
            {
                // The new word might have merged with a following word
                var word = ExtractHorizontalWord(startRow, startCol);
                if (word != null && IsAccidentalWord(word))
                {
                    var wordKey = $"{word.Text}-{word.StartRow}-{word.StartCol}-{word.Direction}";
                    if (!detectedWords.Contains(wordKey))
                    {
                        word.IsValidSwedishWord = dictionary.IsValidWord(word.Text);
                        accidentalWords.Add(word);
                        detectedWords.Add(wordKey);
                    }
                }
            }
        }
        else // Direction.Down
        {
            // Check cell before word start
            if (startRow > 0)
            {
                int checkRow = startRow - 1;
                if (GetCell(checkRow, startCol).HasLetter)
                {
                    // Find start of vertical word
                    int vertStart = checkRow;
                    while (vertStart > 0 && GetCell(vertStart - 1, startCol).HasLetter)
                    {
                        vertStart--;
                    }
                    var word = ExtractVerticalWord(vertStart, startCol);
                    if (word != null && IsAccidentalWord(word))
                    {
                        var wordKey = $"{word.Text}-{word.StartRow}-{word.StartCol}-{word.Direction}";
                        if (!detectedWords.Contains(wordKey))
                        {
                            word.IsValidSwedishWord = dictionary.IsValidWord(word.Text);
                            accidentalWords.Add(word);
                            detectedWords.Add(wordKey);
                        }
                    }
                }
            }
            
            // Check cell after word end
            int endRow = startRow + length;
            if (endRow < Height && GetCell(endRow, startCol).HasLetter)
            {
                // The new word might have merged with a following word
                var word = ExtractVerticalWord(startRow, startCol);
                if (word != null && IsAccidentalWord(word))
                {
                    var wordKey = $"{word.Text}-{word.StartRow}-{word.StartCol}-{word.Direction}";
                    if (!detectedWords.Contains(wordKey))
                    {
                        word.IsValidSwedishWord = dictionary.IsValidWord(word.Text);
                        accidentalWords.Add(word);
                        detectedWords.Add(wordKey);
                    }
                }
            }
        }

        return accidentalWords;
    }

    private AccidentalWord? ExtractHorizontalWord(int startRow, int startCol)
    {
        if (!IsValidPosition(startRow, startCol) || !GetCell(startRow, startCol).HasLetter)
            return null;

        // Check if this is actually the start of a word (not in the middle)
        // A word starts here if the cell to the left is empty or blocked
        if (startCol > 0 && GetCell(startRow, startCol - 1).HasLetter)
            return null; // This is in the middle of a word, not the start

        var sb = new StringBuilder();
        int col = startCol;

        // Extract the word
        while (col < Width && GetCell(startRow, col).HasLetter)
        {
            sb.Append(GetCell(startRow, col).Letter);
            col++;
        }

        string wordText = sb.ToString();
        
        // Any sequence of 2 or more letters is a potential word that needs validation
        if (wordText.Length >= 2)
        {
            return new AccidentalWord
            {
                Text = wordText,
                StartRow = startRow,
                StartCol = startCol,
                Direction = Direction.Across,
                Length = wordText.Length
            };
        }

        return null;
    }

    private AccidentalWord? ExtractVerticalWord(int startRow, int startCol)
    {
        if (!IsValidPosition(startRow, startCol) || !GetCell(startRow, startCol).HasLetter)
            return null;

        // Check if this is actually the start of a word (not in the middle)
        // A word starts here if the cell above is empty or blocked
        if (startRow > 0 && GetCell(startRow - 1, startCol).HasLetter)
            return null; // This is in the middle of a word, not the start

        var sb = new StringBuilder();
        int row = startRow;

        // Extract the word
        while (row < Height && GetCell(row, startCol).HasLetter)
        {
            sb.Append(GetCell(row, startCol).Letter);
            row++;
        }

        string wordText = sb.ToString();
        
        // Any sequence of 2 or more letters is a potential word that needs validation
        if (wordText.Length >= 2)
        {
            return new AccidentalWord
            {
                Text = wordText,
                StartRow = startRow,
                StartCol = startCol,
                Direction = Direction.Down,
                Length = wordText.Length
            };
        }

        return null;
    }

    private bool IsAccidentalWord(AccidentalWord accWord)
    {
        // Check if this word is already an intentionally placed word
        return !_words.Any(w => 
            w.StartRow == accWord.StartRow && 
            w.StartColumn == accWord.StartCol && 
            w.Direction == accWord.Direction &&
            w.Text.Equals(accWord.Text, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Validates the crossword and returns validation result
    /// </summary>
    public CrosswordValidationResult ValidateCrossword(Services.SwedishDictionary? dictionary = null)
    {
        var accidentalWords = DetectAccidentalWords(dictionary);
        
        var result = new CrosswordValidationResult
        {
            IsValid = true,
            AccidentalWords = accidentalWords,
            ValidAccidentalWords = accidentalWords.Where(w => w.IsValidSwedishWord == true).ToList(),
            InvalidAccidentalWords = accidentalWords.Where(w => w.IsValidSwedishWord == false).ToList()
        };

        // If we have a dictionary and found valid accidental words, renumber to include them
        // This ensures the AccidentalWord instances in the result have proper PuzzleNumber values
        if (dictionary != null && result.ValidAccidentalWords.Any(w => w.ShouldIncludeInPuzzle))
        {
            RenumberCluesIncludingAccidental(result.ValidAccidentalWords);
        }

        // Add validation messages
        if (result.InvalidAccidentalWords.Any())
        {
            result.IsValid = false;
            result.Errors.Add($"Hittat {result.InvalidAccidentalWords.Count} ogiltiga oavsiktliga ord");
        }

        if (result.ValidAccidentalWords.Any(w => w.ShouldIncludeInPuzzle))
        {
            result.Warnings.Add($"Inkluderat {result.ValidAccidentalWords.Count(w => w.ShouldIncludeInPuzzle)} giltiga oavsiktliga ord som ledtrådar");
        }

        return result;
    }

    /// <summary>
    /// Promotes valid accidental words to be included as puzzle clues
    /// </summary>
    public void IncludeValidAccidentalWords(Services.SwedishDictionary dictionary)
    {
        var accidentalWords = DetectAccidentalWords(dictionary);
        var validAccidentalWords = accidentalWords.Where(w => w.IsValidSwedishWord == true).ToList();
        
        foreach (var accWord in validAccidentalWords)
        {
            // Only include if it's not already a placed intentional word at the same position
            bool isAlreadyIntentional = Words.Any(w => 
                w.StartRow == accWord.StartRow && 
                w.StartColumn == accWord.StartCol && 
                w.Direction == accWord.Direction &&
                w.Text.Equals(accWord.Text, StringComparison.OrdinalIgnoreCase));

            if (!isAlreadyIntentional)
            {
                // Find the word in dictionary to get its clue
                var dictionaryWords = dictionary.AllWords.Where(w => 
                    w.Text.Equals(accWord.Text, StringComparison.OrdinalIgnoreCase));
                
                if (dictionaryWords.Any())
                {
                    var dictWord = dictionaryWords.First();
                    accWord.ClueFromDictionary = dictWord.Clue;
                    accWord.ShouldIncludeInPuzzle = true;
                }
            }
        }
        
        // Renumber clues including the new accidental words
        RenumberCluesIncludingAccidental(validAccidentalWords);
    }
    
    /// <summary>
    /// Checks whether a straight word's cells are spatially contained within another
    /// straight word's cells (same direction, all cells of the inner word fall within
    /// the outer word's cell range). Works for different start positions.
    /// </summary>
    public static bool IsStraightWordContainedIn(
        int innerRow, int innerCol, Direction innerDir, int innerLen,
        int outerRow, int outerCol, Direction outerDir, int outerLen)
    {
        if (innerDir != outerDir || innerLen >= outerLen) return false;

        if (innerDir == Direction.Across)
        {
            return innerRow == outerRow &&
                   innerCol >= outerCol &&
                   (innerCol + innerLen) <= (outerCol + outerLen);
        }
        else
        {
            return innerCol == outerCol &&
                   innerRow >= outerRow &&
                   (innerRow + innerLen) <= (outerRow + outerLen);
        }
    }

    /// <summary>
    /// Checks whether all cells of the inner Word fall within the outer Word's cells.
    /// Handles bent words (vinkelord) correctly by using actual grid positions
    /// instead of assuming a straight-line projection.
    /// </summary>
    public static bool IsWordContainedInOther(Word inner, Word outer)
    {
        if (!inner.IsPlaced || !outer.IsPlaced) return false;
        if (inner.Length >= outer.Length) return false;

        if (!inner.IsBent && !outer.IsBent)
        {
            return IsStraightWordContainedIn(
                inner.StartRow, inner.StartColumn, inner.Direction, inner.Length,
                outer.StartRow, outer.StartColumn, outer.Direction, outer.Length);
        }

        var outerPositions = outer.GetPositions().ToHashSet();
        return inner.GetPositions().All(p => outerPositions.Contains(p));
    }

    /// <summary>
    /// Checks whether a straight word span is fully contained within a (possibly bent) Word's cells.
    /// </summary>
    public static bool IsStraightSpanContainedInWord(
        int innerRow, int innerCol, Direction innerDir, int innerLen,
        Word outer)
    {
        if (!outer.IsPlaced || innerLen >= outer.Length) return false;

        if (!outer.IsBent)
        {
            return IsStraightWordContainedIn(
                innerRow, innerCol, innerDir, innerLen,
                outer.StartRow, outer.StartColumn, outer.Direction, outer.Length);
        }

        var outerPositions = outer.GetPositions().ToHashSet();
        for (int i = 0; i < innerLen; i++)
        {
            int row = innerDir == Direction.Across ? innerRow : innerRow + i;
            int col = innerDir == Direction.Across ? innerCol + i : innerCol;
            if (!outerPositions.Contains((row, col)))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Checks whether a (possibly bent) Word's cells are fully contained within a straight word span.
    /// </summary>
    public static bool IsWordContainedInStraightSpan(
        Word inner,
        int outerRow, int outerCol, Direction outerDir, int outerLen)
    {
        if (!inner.IsPlaced || inner.Length >= outerLen) return false;

        if (!inner.IsBent)
        {
            return IsStraightWordContainedIn(
                inner.StartRow, inner.StartColumn, inner.Direction, inner.Length,
                outerRow, outerCol, outerDir, outerLen);
        }

        foreach (var (row, col) in inner.GetPositions())
        {
            if (outerDir == Direction.Across)
            {
                if (row != outerRow || col < outerCol || col >= outerCol + outerLen)
                    return false;
            }
            else
            {
                if (col != outerCol || row < outerRow || row >= outerRow + outerLen)
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Renumbers clues including valid accidental words that should be part of the puzzle.
    /// This assigns proper clue numbers to accidental words based on their starting position.
    /// Words fully contained within other words in the same direction are excluded from numbering.
    /// </summary>
    public void RenumberCluesIncludingAccidental(List<AccidentalWord>? accidentalWords = null)
    {
        // First, do normal renumbering for intentional words
        // Clear all existing numbers
        for (int row = 0; row < Height; row++)
        {
            for (int col = 0; col < Width; col++)
            {
                GetCell(row, col).Number = 0;
            }
        }

        // Clear word numbers
        foreach (var word in _words)
        {
            word.Number = 0;
        }

        // Clear accidental word puzzle numbers to ensure fresh numbering
        if (accidentalWords != null)
        {
            foreach (var accWord in accidentalWords)
            {
                accWord.PuzzleNumber = 0;
            }
        }

        // Identify intentional words fully contained within other words
        // Uses spatial containment: all cells of the shorter word fall within the
        // longer word's cell range (same direction, not necessarily same start position).
        var containedWordIds = new HashSet<string>();
        foreach (var word in _words.Where(w => w.IsPlaced))
        {
            bool isContained = _words.Any(other =>
                other.Id != word.Id &&
                other.IsPlaced &&
                IsWordContainedInOther(word, other));

            // Also check against accidental words
            if (!isContained && accidentalWords != null)
            {
                isContained = accidentalWords.Any(acc =>
                    acc.ShouldIncludeInPuzzle &&
                    IsWordContainedInStraightSpan(word,
                        acc.StartRow, acc.StartCol, acc.Direction, acc.Length));
            }

            if (isContained)
            {
                containedWordIds.Add(word.Id);
            }
        }

        // Collect all word start positions (intentional words, excluding contained ones)
        var allWordStarts = new List<(int Row, int Col, Direction Dir, object WordRef)>();

        foreach (var word in _words.Where(w => w.IsPlaced))
        {
            if (containedWordIds.Contains(word.Id))
                continue;

            allWordStarts.Add((word.StartRow, word.StartColumn, word.Direction, word));
        }

        // Add accidental words that should be included
        if (accidentalWords != null)
        {
            foreach (var accWord in accidentalWords.Where(w => w.ShouldIncludeInPuzzle))
            {
                // Check this accidental word isn't already covered by an intentional word
                // with the exact same text. An accidental word that extends an intentional
                // word (same position/direction but longer text) must still be numbered so
                // that GetAllClues can supersede the shorter intentional word.
                bool isAlreadyIntentional = _words.Any(w => 
                    w.StartRow == accWord.StartRow && 
                    w.StartColumn == accWord.StartCol && 
                    w.Direction == accWord.Direction &&
                    w.Text.Equals(accWord.Text, StringComparison.OrdinalIgnoreCase));

                if (isAlreadyIntentional)
                    continue;

                // Check if spatially contained in an intentional word
                bool isContainedInIntentional = _words.Any(w =>
                    w.IsPlaced &&
                    !containedWordIds.Contains(w.Id) &&
                    IsStraightSpanContainedInWord(
                        accWord.StartRow, accWord.StartCol, accWord.Direction, accWord.Length,
                        w));

                if (isContainedInIntentional)
                    continue;

                // Check if spatially contained in another accidental word
                bool isContainedInAccidental = accidentalWords.Any(other =>
                    other != accWord &&
                    other.ShouldIncludeInPuzzle &&
                    IsStraightWordContainedIn(
                        accWord.StartRow, accWord.StartCol, accWord.Direction, accWord.Length,
                        other.StartRow, other.StartCol, other.Direction, other.Length));

                if (isContainedInAccidental)
                    continue;

                allWordStarts.Add((accWord.StartRow, accWord.StartCol, accWord.Direction, accWord));
            }
        }
        
        // Group by position and sort by reading order (top to bottom, left to right)
        var groupedByPosition = allWordStarts
            .GroupBy(w => (w.Row, w.Col))
            .OrderBy(g => g.Key.Row)
            .ThenBy(g => g.Key.Col)
            .ToList();

        int currentNumber = 1;
        
        foreach (var group in groupedByPosition)
        {
            var (row, col) = group.Key;
            
            // Assign number to all words starting at this position
            foreach (var item in group)
            {
                switch (item.WordRef)
                {
                    case Word intentionalWord:
                        intentionalWord.Number = currentNumber;
                        break;
                    case AccidentalWord accidentalWord:
                        accidentalWord.PuzzleNumber = currentNumber;
                        break;
                }
            }
            
            // Set grid cell number
            GetCell(row, col).Number = currentNumber;
            currentNumber++;
        }
    }

    /// <summary>
    /// Fills all empty cells with asterisks to indicate completed crossword areas
    /// Call this after a valid crossword has been generated
    /// </summary>
    public void FillEmptyCellsWithAsterisks()
    {
        for (int row = 0; row < Height; row++)
        {
            for (int col = 0; col < Width; col++)
            {
                var cell = GetCell(row, col);
                if (cell.IsEmpty) // Not blocked, not filled with a letter
                {
                    // Mark the cell as filled with asterisk (but not as part of any word)
                    cell.Letter = '*';
                    cell.IsPartOfWord = false; // Asterisks are not part of words
                    // Don't add to any word IDs
                }
            }
        }
    }

    /// <summary>
    /// Checks if placing a word would connect it to existing words through intersections
    /// </summary>
    private bool WouldConnectToExistingWords(Word word, int startRow, int startCol, Direction direction)
    {
        // Check if this word would share at least one cell with an existing word
        for (int i = 0; i < word.Length; i++)
        {
            int row = direction == Direction.Across ? startRow : startRow + i;
            int col = direction == Direction.Across ? startCol + i : startCol;
            
            var cell = GetCell(row, col);
            
            // If the cell already has a letter and the letters match, this creates a connection
            if (cell.HasLetter && cell.Letter == word.GetCharAt(i))
            {
                return true; // Found at least one intersection
            }
        }
        
        return false; // No intersections found - would be isolated
    }

    /// <summary>
    /// Checks if placing a word would create invalid accidental words
    /// </summary>
    public bool WouldCreateInvalidWords(Word word, int startRow, int startCol, Direction direction, Services.SwedishDictionary dictionary)
    {
        // Temporarily place the word
        var tempGrid = this; // We'll work with current grid
        
        // Check if we can place it first
        if (!CanPlaceWord(word, startRow, startCol, direction))
            return true; // Can't place = invalid
            
        // Use the validation-enabled placement method
        return !TryPlaceWordWithValidation(word, startRow, startCol, direction, dictionary, rejectInvalidWords: true);
    }

    #region Bent Word (Vinkelord) Placement

    /// <summary>
    /// Checks if a bent word can be placed using the given segments.
    /// Each segment must be within bounds, letters must match existing grid content,
    /// and adjacent segments must share a bend cell where the last cell of segment[i]
    /// equals the first cell of segment[i+1].
    /// </summary>
    public bool CanPlaceBentWord(Word word, List<WordSegment> segments)
    {
        if (segments.Count < 2)
            return false; // Not a bent word

        // Validate segment connectivity and direction alternation
        for (int s = 1; s < segments.Count; s++)
        {
            var prev = segments[s - 1];
            var curr = segments[s];

            // Adjacent segments must have different directions
            if (prev.Direction == curr.Direction)
                return false;

            // The last cell of prev must be the first cell of curr (shared bend cell)
            if (prev.EndRow != curr.StartRow || prev.EndCol != curr.StartCol)
                return false;
        }

        // Validate total character count matches word length
        int totalChars = segments[0].Length;
        for (int s = 1; s < segments.Count; s++)
            totalChars += segments[s].Length - 1; // -1 for shared bend cell
        if (totalChars != word.Length)
            return false;

        // Walk all positions and validate against the grid
        int charIdx = 0;
        for (int segIdx = 0; segIdx < segments.Count; segIdx++)
        {
            var segment = segments[segIdx];
            var positions = segment.GetPositions().ToList();

            int start = segIdx == 0 ? 0 : 1; // Skip shared bend cell for subsequent segments
            for (int i = start; i < positions.Count; i++)
            {
                var (row, col) = positions[i];

                if (!IsValidPosition(row, col))
                    return false;

                var cell = GetCell(row, col);
                if (cell.IsBlocked)
                    return false;

                if (cell.HasLetter && cell.Letter != word.GetCharAt(charIdx))
                    return false;

                charIdx++;
            }
        }

        // Check isolation: no adjacent letters in the word's starting direction before the first cell
        var firstSeg = segments[0];
        if (firstSeg.Direction == Direction.Across)
        {
            if (firstSeg.StartCol > 0 && GetCell(firstSeg.StartRow, firstSeg.StartCol - 1).HasLetter)
                return false;
        }
        else
        {
            if (firstSeg.StartRow > 0 && GetCell(firstSeg.StartRow - 1, firstSeg.StartCol).HasLetter)
                return false;
        }

        // Check isolation: no adjacent letters after the last cell in the last segment's direction
        var lastSeg = segments[^1];
        if (lastSeg.Direction == Direction.Across)
        {
            if (lastSeg.EndCol + 1 < Width && GetCell(lastSeg.EndRow, lastSeg.EndCol + 1).HasLetter)
                return false;
        }
        else
        {
            if (lastSeg.EndRow + 1 < Height && GetCell(lastSeg.EndRow + 1, lastSeg.EndCol).HasLetter)
                return false;
        }

        // Check that no new (empty) cell in this bent word lands immediately after the
        // endpoint of any existing word in any direction, and that no cell lands
        // immediately before the first cell of any existing bent word's first segment.
        for (int segIdx = 0; segIdx < segments.Count; segIdx++)
        {
            var seg = segments[segIdx];
            var positions = seg.GetPositions().ToList();
            int start = segIdx == 0 ? 0 : 1; // skip shared bend cell for subsequent segments
            for (int i = start; i < positions.Count; i++)
            {
                var (cellRow, cellCol) = positions[i];
                if (!GetCell(cellRow, cellCol).HasLetter && WouldFollowAnyWordEnd(cellRow, cellCol))
                    return false;

                if (!GetCell(cellRow, cellCol).HasLetter && WouldPrecedeAnyBentWordStart(cellRow, cellCol))
                    return false;
            }
        }

        // A bend cell will carry a direction arrow. If that cell is the terminal cell of
        // an existing word, readers of that word would follow the arrow and continue
        // reading past the word's actual end (e.g. KANAL ending at 'L' then a bend arrow
        // placed on that 'L' makes it read as KANALUT).
        for (int s = 0; s < segments.Count - 1; s++)
        {
            int bendRow = segments[s].EndRow;
            int bendCol = segments[s].EndCol;
            foreach (var w in _words)
            {
                if (w.IsPlaced && w.EndRow == bendRow && w.EndColumn == bendCol)
                    return false;
            }
        }

        // The terminal cell of the new word must not already carry a BendArrowDirection.
        // If it does, an existing bent word bends there, and its arrow would mislead
        // readers of the new word into continuing past its actual end.
        if (GetCell(segments[^1].EndRow, segments[^1].EndCol).BendArrowDirection != null)
            return false;

        return true;
    }

    /// <summary>
    /// Attempts to place a bent word with validation to prevent invalid accidental words.
    /// Similar to TryPlaceWordWithValidation but for multi-segment bent words.
    /// </summary>
    public bool TryPlaceBentWordWithValidation(Word word, List<WordSegment> segments,
        Services.SwedishDictionary? dictionary = null, bool rejectInvalidWords = true)
    {
        if (!CanPlaceBentWord(word, segments))
            return false;

        // DUPLICATE CHECK
        var wordTextUpper = word.Text.ToUpperInvariant();
        foreach (var existingWord in _words)
        {
            if (existingWord.Text.Equals(wordTextUpper, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // CONNECTIVITY CHECK: bent words must connect to existing words if any exist
        if (_words.Count > 0)
        {
            bool connects = false;
            int charIdx = 0;
            for (int segIdx = 0; segIdx < segments.Count && !connects; segIdx++)
            {
                var positions = segments[segIdx].GetPositions().ToList();
                int start = segIdx == 0 ? 0 : 1;
                for (int i = start; i < positions.Count; i++)
                {
                    var (row, col) = positions[i];
                    var cell = GetCell(row, col);
                    if (cell.HasLetter && cell.Letter == word.GetCharAt(charIdx))
                    {
                        connects = true;
                        break;
                    }
                    charIdx++;
                }
            }
            if (!connects)
                return false;
        }

        var originalState = CreateGridBackup();

        try
        {
            // Temporarily place
            word.StartRow = segments[0].StartRow;
            word.StartColumn = segments[0].StartCol;
            word.Direction = segments[0].Direction;
            word.Segments = new List<WordSegment>(segments);
            word.IsPlaced = true;

            int ci = 0;
            for (int segIdx = 0; segIdx < segments.Count; segIdx++)
            {
                var positions = segments[segIdx].GetPositions().ToList();
                int start = segIdx == 0 ? 0 : 1;
                for (int i = start; i < positions.Count; i++)
                {
                    var (row, col) = positions[i];
                    GetCell(row, col).SetLetter(word.GetCharAt(ci), word.Id);
                    ci++;
                }
            }

            for (int s = 0; s < segments.Count - 1; s++)
            {
                GetCell(segments[s].EndRow, segments[s].EndCol).BendArrowDirection = segments[s + 1].Direction;
            }

            _words.Add(word);

            bool isValid = true;
            if (dictionary != null && rejectInvalidWords)
            {
                // Check accidental words near each segment
                var allAccidental = new List<AccidentalWord>();
                var detectedKeys = new HashSet<string>();

                foreach (var seg in segments)
                {
                    var near = DetectAccidentalWordsNear(seg.StartRow, seg.StartCol, seg.Direction, seg.Length, dictionary);
                    foreach (var aw in near)
                    {
                        var key = $"{aw.Text}-{aw.StartRow}-{aw.StartCol}-{aw.Direction}";
                        if (detectedKeys.Add(key))
                            allAccidental.Add(aw);
                    }
                }

                foreach (var accWord in allAccidental)
                {
                    if (accWord.IsValidSwedishWord == false)
                    {
                        isValid = false;
                        break;
                    }
                }

                if (isValid)
                {
                    var existingWordTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var w in _words)
                    {
                        if (w.Id != word.Id)
                            existingWordTexts.Add(w.Text);
                    }

                    foreach (var accWord in allAccidental)
                    {
                        if (accWord.IsValidSwedishWord == true && existingWordTexts.Contains(accWord.Text))
                        {
                            isValid = false;
                            break;
                        }
                    }
                }
            }

            if (isValid)
            {
                RenumberCluesIncludingAccidental(null);
                return true;
            }
            else
            {
                RestoreGridFromBackup(originalState);
                return false;
            }
        }
        catch
        {
            RestoreGridFromBackup(originalState);
            return false;
        }
    }

    #endregion
}