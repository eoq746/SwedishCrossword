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
                var invalidWords = accidentalWords.Where(w => w.IsValidSwedishWord == false).ToList();
                
                // Reject if we created any invalid accidental words
                if (invalidWords.Any())
                {
                    isValid = false;
                }
                
                // Also reject if any accidental word would duplicate an existing intentional word's text
                // This prevents the same word appearing twice in the puzzle
                if (isValid)
                {
                    var existingWordTexts = _words
                        .Where(w => w.Id != word.Id) // Exclude the word we just placed
                        .Select(w => w.Text.ToUpperInvariant())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    
                    var duplicateAccidentalWords = accidentalWords
                        .Where(a => a.IsValidSwedishWord == true)
                        .Where(a => existingWordTexts.Contains(a.Text.ToUpperInvariant()))
                        .ToList();
                    
                    if (duplicateAccidentalWords.Any())
                    {
                        // This placement would create an accidental word that duplicates an existing word
                        isValid = false;
                    }
                }
            }

            if (isValid)
            {
                // Placement is valid - renumber clues and return success
                RenumberClues();
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
                    IsPartOfWord = cell.IsPartOfWord
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

        return true;
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
        RenumberClues();
        
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
        RenumberClues();

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

        // Group words by starting position
        var wordsByPosition = _words
            .Where(w => w.IsPlaced)
            .GroupBy(w => (w.StartRow, w.StartColumn))
            .OrderBy(g => g.Key.StartRow)
            .ThenBy(g => g.Key.StartColumn);

        int currentNumber = 1;
        foreach (var group in wordsByPosition)
        {
            var (row, col) = group.Key;
            
            // Assign the same number to all words starting at this position
            foreach (var word in group)
            {
                word.Number = currentNumber;
            }
            
            // Set the grid cell number
            GetCell(row, col).Number = currentNumber;
            
            currentNumber++;
        }
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
            FillPercentage = fillPercentage
        };
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
    /// Renumbers clues including valid accidental words that should be part of the puzzle.
    /// This assigns proper clue numbers to accidental words based on their starting position.
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

        // Collect all word start positions (intentional words)
        var allWordStarts = new List<(int Row, int Col, Direction Dir, object WordRef)>();
        
        foreach (var word in _words.Where(w => w.IsPlaced))
        {
            allWordStarts.Add((word.StartRow, word.StartColumn, word.Direction, word));
        }
        
        // Add accidental words that should be included
        if (accidentalWords != null)
        {
            foreach (var accWord in accidentalWords.Where(w => w.ShouldIncludeInPuzzle))
            {
                // Check this accidental word isn't already covered by an intentional word
                bool isAlreadyIntentional = _words.Any(w => 
                    w.StartRow == accWord.StartRow && 
                    w.StartColumn == accWord.StartCol && 
                    w.Direction == accWord.Direction);
                    
                if (!isAlreadyIntentional)
                {
                    allWordStarts.Add((accWord.StartRow, accWord.StartCol, accWord.Direction, accWord));
                }
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
}