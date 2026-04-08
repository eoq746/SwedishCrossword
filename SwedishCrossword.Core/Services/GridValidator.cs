using SwedishCrossword.Models;

namespace SwedishCrossword.Services;

/// <summary>
/// Service for validating crossword grids and ensuring puzzle quality
/// </summary>
public class GridValidator
{
    /// <summary>
    /// Validates that a crossword grid meets basic quality standards
    /// </summary>
    public bool IsValidCrossword(CrosswordGrid grid)
    {
        if (grid == null || grid.Words.Count == 0)
            return false;

        var validationResult = ValidateGrid(grid);
        return validationResult.IsValid;
    }

    /// <summary>
    /// Performs comprehensive validation of a crossword grid
    /// </summary>
    public ValidationResult ValidateGrid(CrosswordGrid grid)
    {
        var result = new ValidationResult();

        if (grid == null)
        {
            result.AddError("Grid cannot be null");
            return result;
        }

        // Basic structure validation
        ValidateBasicStructure(grid, result);
        
        // Word placement validation
        ValidateWordPlacements(grid, result);
        
        // Intersection validation
        ValidateIntersections(grid, result);
        
        // Connectivity validation
        ValidateConnectivity(grid, result);
        
        // Quality metrics
        ValidateQualityMetrics(grid, result);

        return result;
    }

    private void ValidateBasicStructure(CrosswordGrid grid, ValidationResult result)
    {
        // Check minimum grid size
        if (grid.Width < 5 || grid.Height < 5)
        {
            result.AddError($"Grid too small ({grid.Width}x{grid.Height}). Minimum size is 5x5");
        }

        // Check maximum grid size (for practical purposes)
        if (grid.Width > 25 || grid.Height > 25)
        {
            result.AddWarning($"Grid very large ({grid.Width}x{grid.Height}). May be difficult to print");
        }

        // Make word count more lenient - treat as warning for small counts
        if (grid.Words.Count == 0)
        {
            result.AddError("No words placed on grid");
        }
        else if (grid.Words.Count < 3)
        {
            result.AddWarning($"Few words placed ({grid.Words.Count}). Ideally should have at least 3 words");
        }
    }

    private void ValidateWordPlacements(CrosswordGrid grid, ValidationResult result)
    {
        foreach (var word in grid.Words)
        {
            // Check if word is properly placed
            if (!word.IsPlaced)
            {
                result.AddError($"Word '{word.Text}' is not placed on grid");
                continue;
            }

            // Check bounds
            if (!IsWordWithinBounds(grid, word))
            {
                result.AddError($"Word '{word.Text}' extends outside grid bounds");
                continue;
            }

            // Check that all letters are properly placed
            if (!ValidateWordLetters(grid, word))
            {
                result.AddError($"Word '{word.Text}' has incorrect letters on grid");
            }

            // Check isolation (no adjacent words except at intersections) - make this less strict
            if (!IsWordProperlyIsolated(grid, word))
            {
                result.AddInfo($"Word '{word.Text}' has minor isolation concerns");
            }
        }
    }

    private bool IsWordWithinBounds(CrosswordGrid grid, Word word)
    {
        return word.StartRow >= 0 && word.StartColumn >= 0 &&
               word.EndRow < grid.Height && word.EndColumn < grid.Width;
    }

    private bool ValidateWordLetters(CrosswordGrid grid, Word word)
    {
        var positions = word.GetPositions().ToList();
        
        for (int i = 0; i < positions.Count; i++)
        {
            var (row, col) = positions[i];
            var cell = grid.GetCell(row, col);
            
            if (cell.Letter != word.GetCharAt(i))
            {
                return false;
            }

            if (!cell.WordIds.Contains(word.Id))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsWordProperlyIsolated(CrosswordGrid grid, Word word)
    {
        var positions = word.GetPositions().ToHashSet();

        // Check all positions around the word
        foreach (var (row, col) in positions)
        {
            var adjacentPositions = GetAdjacentPositions(row, col, word.Direction);
            
            foreach (var (adjRow, adjCol) in adjacentPositions)
            {
                if (!grid.IsValidPosition(adjRow, adjCol))
                    continue;

                var cell = grid.GetCell(adjRow, adjCol);
                
                // If there's a letter in an adjacent cell, it should be part of an intersecting word
                if (cell.HasLetter && !positions.Contains((adjRow, adjCol)))
                {
                    // Check if this is a valid intersection
                    if (!IsValidIntersection(grid, word, adjRow, adjCol))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private IEnumerable<(int Row, int Column)> GetAdjacentPositions(int row, int col, Direction wordDirection)
    {
        // For across words, check above and below
        // For down words, check left and right
        if (wordDirection == Direction.Across)
        {
            yield return (row - 1, col); // Above
            yield return (row + 1, col); // Below
        }
        else
        {
            yield return (row, col - 1); // Left
            yield return (row, col + 1); // Right
        }
    }

    private bool IsValidIntersection(CrosswordGrid grid, Word word, int row, int col)
    {
        var cell = grid.GetCell(row, col);
        
        // The cell should belong to exactly one other word that runs perpendicular
        var intersectingWords = grid.Words.Where(w => 
            w != word && 
            w.Direction != word.Direction &&
            w.GetPositions().Contains((row, col))
        ).ToList();

        return intersectingWords.Count == 1;
    }

    private void ValidateIntersections(CrosswordGrid grid, ValidationResult result)
    {
        var intersectionCount = 0;
        var wordPairs = new HashSet<(string, string)>();

        foreach (var word1 in grid.Words)
        {
            foreach (var word2 in grid.Words)
            {
                if (word1.Id == word2.Id || word1.Direction == word2.Direction)
                    continue;

                // Avoid duplicate checking
                var pairKey = string.Compare(word1.Id, word2.Id) < 0 
                    ? (word1.Id, word2.Id) 
                    : (word2.Id, word1.Id);
                
                if (wordPairs.Contains(pairKey))
                    continue;
                
                wordPairs.Add(pairKey);

                var intersections = word1.GetIntersections(word2).ToList();
                
                foreach (var (row, col, myIdx, otherIdx) in intersections)
                {
                    intersectionCount++;
                    
                    // Validate that the letters match
                    if (word1.GetCharAt(myIdx) != word2.GetCharAt(otherIdx))
                    {
                        result.AddError($"Letter mismatch at intersection ({row},{col}) between '{word1.Text}' and '{word2.Text}'");
                    }
                }
            }
        }

        // Check if there are enough intersections for connectivity - make this more lenient
        var minimumIntersections = Math.Max(0, grid.Words.Count - 2); // Allow for some disconnected words
        if (intersectionCount < minimumIntersections)
        {
            result.AddInfo($"Could benefit from more intersections ({intersectionCount} found). Words may benefit from better connections");
        }
    }

    private void ValidateConnectivity(CrosswordGrid grid, ValidationResult result)
    {
        if (grid.Words.Count <= 1)
            return;

        // Build a graph of word connections
        var wordGraph = BuildWordConnectionGraph(grid);
        
        // Check if all words are connected
        var connectedComponents = FindConnectedComponents(wordGraph);
        
        if (connectedComponents.Count > 1)
        {
            // STRICT ENFORCEMENT: All words must be connected - no isolated words allowed
            result.AddError($"Grid has {connectedComponents.Count} disconnected components. All words must be connected through intersections");
            
            foreach (var (componentIndex, words) in connectedComponents.Select((comp, idx) => (idx, comp)))
            {
                var wordTexts = words.Select(w => w.Text);
                result.AddError($"Disconnected component {componentIndex + 1}: {string.Join(", ", wordTexts)}");
            }
        }

        // Additional check: Ensure every word has at least one intersection (except for single word grids)
        if (grid.Words.Count > 1)
        {
            foreach (var word in grid.Words)
            {
                if (wordGraph[word].Count == 0)
                {
                    result.AddError($"Word '{word.Text}' is isolated - it must intersect with at least one other word");
                }
            }
        }
    }

    private Dictionary<Word, HashSet<Word>> BuildWordConnectionGraph(CrosswordGrid grid)
    {
        var graph = new Dictionary<Word, HashSet<Word>>();
        
        // Initialize graph
        foreach (var word in grid.Words)
        {
            graph[word] = [];
        }

        // Add connections for intersecting words
        foreach (var word1 in grid.Words)
        {
            foreach (var word2 in grid.Words)
            {
                if (word1.Id != word2.Id && word1.IntersectsWith(word2))
                {
                    graph[word1].Add(word2);
                    graph[word2].Add(word1);
                }
            }
        }

        return graph;
    }

    private List<List<Word>> FindConnectedComponents(Dictionary<Word, HashSet<Word>> graph)
    {
        var visited = new HashSet<Word>();
        var components = new List<List<Word>>();

        foreach (var word in graph.Keys)
        {
            if (!visited.Contains(word))
            {
                var component = new List<Word>();
                DepthFirstSearch(word, graph, visited, component);
                components.Add(component);
            }
        }

        return components;
    }

    private void DepthFirstSearch(Word word, Dictionary<Word, HashSet<Word>> graph, HashSet<Word> visited, List<Word> component)
    {
        visited.Add(word);
        component.Add(word);

        foreach (var neighbor in graph[word])
        {
            if (!visited.Contains(neighbor))
            {
                DepthFirstSearch(neighbor, graph, visited, component);
            }
        }
    }

    private void ValidateQualityMetrics(CrosswordGrid grid, ValidationResult result)
    {
        var stats = grid.GetStats();

        // More lenient fill percentage validation - focus on very low percentages
        if (stats.FillPercentage < 25)
        {
            result.AddWarning($"Very low fill percentage ({stats.FillPercentage:F1}%). Consider adding more words");
        }
        else if (stats.FillPercentage > 85)
        {
            result.AddInfo($"High fill percentage ({stats.FillPercentage:F1}%). Grid is well filled");
        }

        // Check word length distribution (only if there are words)
        if (grid.Words.Count > 0)
        {
            var avgLength = grid.Words.Average(w => w.Length);
            if (avgLength < 2.5)
            {
                result.AddInfo($"Average word length is short ({avgLength:F1}). Using many short words");
            }
        }

        // Check for repeated words - this should still be an error
        var duplicates = grid.Words.GroupBy(w => w.Text).Where(g => g.Count() > 1).ToList();
        if (duplicates.Count > 0)
        {
            foreach (var group in duplicates)
            {
                result.AddError($"Duplicate word found: '{group.Key}' appears {group.Count()} times");
            }
        }

        // Check numbering consistency (only if there are words) - make this info only
        if (grid.Words.Count > 0)
        {
            var numberedCells = new List<(int Number, int Row, int Col)>();
            for (int row = 0; row < grid.Height; row++)
            {
                for (int col = 0; col < grid.Width; col++)
                {
                    var cell = grid.GetCell(row, col);
                    if (cell.IsNumbered)
                    {
                        numberedCells.Add((cell.Number, row, col));
                    }
                }
            }

            var expectedNumbers = Enumerable.Range(1, grid.Words.Count).ToHashSet();
            var actualNumbers = grid.Words.Select(w => w.Number).ToHashSet();

            if (!expectedNumbers.SetEquals(actualNumbers))
            {
                result.AddInfo("Word numbering may need adjustment");
            }
        }
    }

    /// <summary>
    /// Quick validation for placement testing with dictionary validation
    /// </summary>
    public bool CanPlaceWordSafelyWithValidation(CrosswordGrid grid, Word word, int row, int col, Direction direction, SwedishDictionary? dictionary = null, bool rejectInvalidWords = true)
    {
        // Basic placement check first
        if (!CanPlaceWordSafely(grid, word, row, col, direction))
            return false;

        // If dictionary validation is requested, check for invalid accidental words
        if (dictionary != null && rejectInvalidWords)
        {
            return !grid.WouldCreateInvalidWords(word, row, col, direction, dictionary);
        }

        return true;
    }

    /// <summary>
    /// Quick validation for placement testing
    /// </summary>
    public bool CanPlaceWordSafely(CrosswordGrid grid, Word word, int row, int col, Direction direction)
    {
        // This is used during generation to quickly check if a placement would be valid
        if (!grid.IsValidPosition(row, col))
            return false;

        var endRow = direction == Direction.Across ? row : row + word.Length - 1;
        var endCol = direction == Direction.Across ? col + word.Length - 1 : col;

        if (!grid.IsValidPosition(endRow, endCol))
            return false;

        // Check each position for conflicts
        for (int i = 0; i < word.Length; i++)
        {
            var checkRow = direction == Direction.Across ? row : row + i;
            var checkCol = direction == Direction.Across ? col + i : col;
            
            var cell = grid.GetCell(checkRow, checkCol);
            
            if (cell.IsBlocked)
                return false;
                
            if (cell.HasLetter && cell.Letter != word.GetCharAt(i))
                return false;
        }

        return true;
    }
}

/// <summary>
/// Result of grid validation with detailed information
/// </summary>
public class ValidationResult
{
    private readonly List<string> _errors = [];
    private readonly List<string> _warnings = [];
    private readonly List<string> _info = [];

    public bool IsValid => _errors.Count == 0; // Only errors make it invalid, warnings are acceptable
    public IReadOnlyList<string> Errors => _errors.AsReadOnly();
    public IReadOnlyList<string> Warnings => _warnings.AsReadOnly();
    public IReadOnlyList<string> Info => _info.AsReadOnly();

    public void AddError(string message) => _errors.Add($"ERROR: {message}");
    public void AddWarning(string message) => _warnings.Add($"WARNING: {message}");
    public void AddInfo(string message) => _info.Add($"INFO: {message}");

    public override string ToString()
    {
        var lines = new List<string>();
        
        if (_errors.Count > 0)
        {
            lines.Add("ERRORS:");
            lines.AddRange(_errors.Select(e => $"  {e}"));
        }

        if (_warnings.Count > 0)
        {
            if (lines.Count > 0) lines.Add("");
            lines.Add("WARNINGS:");
            lines.AddRange(_warnings.Select(w => $"  {w}"));
        }

        if (_info.Count > 0)
        {
            if (lines.Count > 0) lines.Add("");
            lines.Add("INFO:");
            lines.AddRange(_info.Select(i => $"  {i}"));
        }

        if (lines.Count == 0)
        {
            lines.Add("Grid validation passed successfully.");
        }

        return string.Join(Environment.NewLine, lines);
    }
}