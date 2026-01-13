using System.Text;
using SwedishCrossword.Models;

namespace SwedishCrossword.Services;

/// <summary>
/// Service for generating printable output of crossword puzzles
/// </summary>
public class PrintService
{
    private readonly ClueGenerator _clueGenerator;

    public PrintService(ClueGenerator clueGenerator)
    {
        _clueGenerator = clueGenerator ?? throw new ArgumentNullException(nameof(clueGenerator));
    }

    /// <summary>
    /// Generates a complete printable crossword document
    /// </summary>
    public string GeneratePrintableDocument(CrosswordPuzzle puzzle, PrintOptions options)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        ArgumentNullException.ThrowIfNull(options);

        var document = new StringBuilder();

        // Title and header
        if (options.IncludeTitle)
        {
            document.AppendLine(CreateTitle(puzzle, options));
            document.AppendLine();
        }

        // Statistics (if requested)
        if (options.IncludeStatistics)
        {
            document.AppendLine(CreateStatistics(puzzle));
            document.AppendLine();
        }

        // Puzzle grid
        document.AppendLine("KORSORD:");
        document.AppendLine();
        document.AppendLine(CreatePuzzleGrid(puzzle, options));
        document.AppendLine();

        // Clues
        document.AppendLine(CreateCluesList(puzzle, options));

        // Solution (if requested)
        if (options.IncludeSolution)
        {
            document.AppendLine();
            document.AppendLine("LOSNING:");
            document.AppendLine();
            document.AppendLine(CreateSolutionGrid(puzzle, options));
        }

        // Footer
        if (options.IncludeFooter)
        {
            document.AppendLine();
            document.AppendLine(CreateFooter(puzzle));
        }

        return document.ToString();
    }

    private string CreateTitle(CrosswordPuzzle puzzle, PrintOptions options)
    {
        var title = new StringBuilder();
        
        if (!string.IsNullOrEmpty(options.Title))
        {
            title.AppendLine(CenterText(options.Title, 60));
        }
        else
        {
            title.AppendLine(CenterText("SVENSKT KORSORD", 60));
        }

        title.AppendLine(CenterText(new string('=', 60), 60));
        
        if (options.IncludeDate)
        {
            title.AppendLine(CenterText($"Skapat: {puzzle.CreatedAt:yyyy-MM-dd HH:mm}", 60));
        }

        return title.ToString();
    }

    private string CreateStatistics(CrosswordPuzzle puzzle)
    {
        var stats = puzzle.Statistics;
        var sb = new StringBuilder();
        
        sb.AppendLine($"Storlek: {puzzle.Grid.Width} x {puzzle.Grid.Height}");
        sb.AppendLine($"Antal ord: {stats.WordCount}");
        sb.AppendLine($"Fyllnadsgrad: {stats.FillPercentage:F1}%");
        sb.AppendLine($"Genereringsforsoek: {puzzle.GenerationAttempts}");

        return sb.ToString();
    }

    private string CreatePuzzleGrid(CrosswordPuzzle puzzle, PrintOptions options)
    {
        return CreateGrid(puzzle.Grid, showSolution: false, options.GridStyle);
    }

    private string CreateSolutionGrid(CrosswordPuzzle puzzle, PrintOptions options)
    {
        return CreateGrid(puzzle.Grid, showSolution: true, options.GridStyle);
    }

    private string CreateGrid(CrosswordGrid grid, bool showSolution, GridStyle style)
    {
        return style switch
        {
            GridStyle.ASCII => CreateASCIIGrid(grid, showSolution),
            GridStyle.Unicode => CreateUnicodeGrid(grid, showSolution),
            GridStyle.UnicodeCompat => CreateHybridUnicodeGrid(grid, showSolution),
            GridStyle.Simple => CreateSimpleGrid(grid, showSolution),
            _ => CreateASCIIGrid(grid, showSolution)
        };
    }

    /// <summary>
    /// Creates a Unicode grid with fallback to ASCII if Unicode is not supported
    /// </summary>
    public string CreateUnicodeGridSafe(CrosswordGrid grid, bool showSolution)
    {
        // Always use ASCII to avoid compatibility issues
        return CreateASCIIGrid(grid, showSolution);
    }

    /// <summary>
    /// Creates a grid using basic Unicode characters that are more widely supported
    /// </summary>
    private string CreateHybridUnicodeGrid(CrosswordGrid grid, bool showSolution)
    {
        // Use ASCII instead to avoid Unicode compatibility issues
        return CreateASCIIGrid(grid, showSolution);
    }

    private string CreateUnicodeGrid(CrosswordGrid grid, bool showSolution)
    {
        // Always use ASCII since Unicode characters can cause printing issues
        return CreateASCIIGrid(grid, showSolution);
    }

    private string CreateASCIIGrid(CrosswordGrid grid, bool showSolution)
    {
        var sb = new StringBuilder();
        var cellWidth = 5;
        var cellHeight = 2; // Two lines per cell for better spacing

        // Top border
        sb.Append("+");
        for (int col = 0; col < grid.Width; col++)
        {
            sb.Append(new string('-', cellWidth));
            sb.Append("+");
        }
        sb.AppendLine();

        for (int row = 0; row < grid.Height; row++)
        {
            // First line of cell: number in top-left corner
            sb.Append("|");
            for (int col = 0; col < grid.Width; col++)
            {
                var cell = grid.GetCell(row, col);
                string content;

                if (cell.IsBlocked)
                {
                    content = new string('#', cellWidth);
                }
                else if (cell.IsNumbered)
                {
                    // Number in top-left, rest empty
                    var numStr = cell.Number.ToString();
                    content = numStr.PadRight(cellWidth);
                }
                else if (cell.HasAsterisk)
                {
                    content = "*****";
                }
                else
                {
                    content = new string(' ', cellWidth);
                }

                sb.Append(content);
                sb.Append("|");
            }
            sb.AppendLine();

            // Second line of cell: letter centered (for solution) or empty space for writing
            sb.Append("|");
            for (int col = 0; col < grid.Width; col++)
            {
                var cell = grid.GetCell(row, col);
                string content;

                if (cell.IsBlocked)
                {
                    content = new string('#', cellWidth);
                }
                else if (showSolution && cell.HasLetter)
                {
                    // Center the letter
                    content = $"  {cell.Letter}  ";
                }
                else if (cell.HasAsterisk)
                {
                    content = "*****";
                }
                else
                {
                    // Empty space for writing
                    content = new string(' ', cellWidth);
                }

                sb.Append(content);
                sb.Append("|");
            }
            sb.AppendLine();

            // Row separator
            sb.Append("+");
            for (int col = 0; col < grid.Width; col++)
            {
                sb.Append(new string('-', cellWidth));
                sb.Append("+");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Detects if the current console environment supports Unicode box-drawing characters
    /// </summary>
    private static bool SupportsUnicode()
    {
        try
        {
            // Check if we can use UTF8 encoding
            return Console.OutputEncoding.CodePage == Encoding.UTF8.CodePage ||
                   Console.OutputEncoding.EncodingName.Contains("Unicode", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private string CreateSimpleGrid(CrosswordGrid grid, bool showSolution)
    {
        var sb = new StringBuilder();

        for (int row = 0; row < grid.Height; row++)
        {
            for (int col = 0; col < grid.Width; col++)
            {
                var cell = grid.GetCell(row, col);

                if (cell.IsBlocked)
                {
                    sb.Append("##  "); // Hash to indicate blocked cell
                }
                else if (showSolution && cell.HasLetter)
                {
                    sb.Append($" {cell.Letter}  ");
                }
                else if (cell.IsNumbered)
                {
                    sb.Append($"{cell.Number,2}  ");
                }
                else if (cell.HasAsterisk)
                {
                    sb.Append(" *  "); // Show asterisks for empty cells in completed crosswords
                }
                else
                {
                    sb.Append(" _  ");
                }
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private string CreateCluesList(CrosswordPuzzle puzzle, PrintOptions options)
    {
        var (across, down) = GetAllClues(puzzle);
        var sb = new StringBuilder();

        if (across.Count > 0)
        {
            sb.AppendLine("VÅGRÄTT:");
            foreach (var item in across.OrderBy(x => GetNumber(x)))
            {
                var number = GetNumber(item);
                var clue = GetClueText(item, options);
                
                sb.AppendLine($"{number,2}. {clue}");
                
                if (options.ShowWordLength)
                {
                    var length = GetWordLength(item);
                    sb.AppendLine($"    ({length} bokstaver)");
                }
            }
        }

        if (down.Count > 0)
        {
            if (across.Count > 0) sb.AppendLine();
            sb.AppendLine("LODRÄTT:");
            foreach (var item in down.OrderBy(x => GetNumber(x)))
            {
                var number = GetNumber(item);
                var clue = GetClueText(item, options);
                
                sb.AppendLine($"{number,2}. {clue}");
                
                if (options.ShowWordLength)
                {
                    var length = GetWordLength(item);
                    sb.AppendLine($"    ({length} bokstaver)");
                }
            }
        }

        return sb.ToString();
    }

    private string CreateFooter(CrosswordPuzzle puzzle)
    {
        var sb = new StringBuilder();
        sb.AppendLine(new string('=', 60));
        sb.AppendLine(CenterText("Skapad med Svensk Korsord Generator", 60));
        sb.AppendLine(CenterText($"ID: {puzzle.Grid.Words.FirstOrDefault()?.Id?[..8] ?? "N/A"}", 60));
        return sb.ToString();
    }

    /// <summary>
    /// Generates a printer-friendly format optimized for A4 paper
    /// </summary>
    public string GenerateA4PrintFormat(CrosswordPuzzle puzzle, PrintOptions options)
    {
        // Adjust options for A4 printing
        var printOptions = options with
        {
            GridStyle = GridStyle.Unicode,
            IncludeTitle = true,
            IncludeDate = true,
            IncludeFooter = true,
            ShowWordLength = false // Save space
        };

        var document = new StringBuilder();

        // Title section
        document.AppendLine(CreateTitle(puzzle, printOptions));
        document.AppendLine();

        // Calculate layout - try to fit everything on one page
        var gridHeight = puzzle.Grid.Height + 5; // Grid plus borders
        var cluesCount = puzzle.Grid.Words.Count;
        var estimatedCluesHeight = cluesCount + 5; // Rough estimate

        if (gridHeight + estimatedCluesHeight > 50) // Won't fit on one page
        {
            // Grid on page 1
            document.AppendLine("KORSORD:");
            document.AppendLine();
            document.AppendLine(CreateGrid(puzzle.Grid, false, printOptions.GridStyle));
            
            // Page break indicator
            document.AppendLine();
            document.AppendLine(CenterText("--- VAND SIDAN FOR LEDTRADAR ---", 60));
            document.AppendLine(new string('=', 60));
            document.AppendLine();
            
            // Clues on page 2
            document.AppendLine("LEDTRADAR:");
            document.AppendLine();
            document.AppendLine(CreateCluesList(puzzle, printOptions));
        }
        else
        {
            // Everything fits on one page - side by side layout for smaller puzzles
            document.AppendLine(CreateCompactLayout(puzzle, printOptions));
        }

        return document.ToString();
    }

    private string CreateCompactLayout(CrosswordPuzzle puzzle, PrintOptions options)
    {
        var sb = new StringBuilder();
        
        // Grid on the left, clues on the right for small puzzles
        if (puzzle.Grid.Width <= 11 && puzzle.Grid.Height <= 11)
        {
            var gridLines = CreateGrid(puzzle.Grid, false, options.GridStyle).Split('\n');
            var cluesLines = CreateCluesList(puzzle, options).Split('\n');
            
            var maxLines = Math.Max(gridLines.Length, cluesLines.Length);
            
            for (int i = 0; i < maxLines; i++)
            {
                var gridLine = i < gridLines.Length ? gridLines[i].PadRight(45) : new string(' ', 45);
                var clueLine = i < cluesLines.Length ? cluesLines[i] : "";
                
                sb.AppendLine($"{gridLine} {clueLine}");
            }
        }
        else
        {
            // Standard layout for larger puzzles
            sb.AppendLine("KORSORD:");
            sb.AppendLine();
            sb.AppendLine(CreateGrid(puzzle.Grid, false, options.GridStyle));
            sb.AppendLine();
            sb.AppendLine(CreateCluesList(puzzle, options));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Saves the puzzle to a file
    /// </summary>
    public async Task SaveToFileAsync(CrosswordPuzzle puzzle, string filePath, PrintOptions options)
    {
        var content = GeneratePrintableDocument(puzzle, options);
        // Explicitly use UTF-8 with BOM to ensure proper display of Swedish characters
        await File.WriteAllTextAsync(filePath, content, new UTF8Encoding(true));
    }

    /// <summary>
    /// Saves the puzzle in A4 print format
    /// </summary>
    public async Task SaveA4PrintAsync(CrosswordPuzzle puzzle, string filePath, PrintOptions options)
    {
        var content = GenerateA4PrintFormat(puzzle, options);
        // Explicitly use UTF-8 with BOM to ensure proper display of Swedish characters
        await File.WriteAllTextAsync(filePath, content, new UTF8Encoding(true));
    }

    /// <summary>
    /// Generates JSON data for the interactive web crossword
    /// </summary>
    public string GenerateJsonForWeb(CrosswordPuzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        
        var grid = puzzle.Grid;
        var (across, down) = GetAllClues(puzzle);
        
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"width\": {grid.Width},");
        sb.AppendLine($"  \"height\": {grid.Height},");
        sb.AppendLine($"  \"createdAt\": \"{puzzle.CreatedAt:yyyy-MM-dd HH:mm}\",");
        sb.AppendLine($"  \"wordCount\": {puzzle.Statistics.WordCount},");
        sb.AppendLine($"  \"fillPercentage\": {puzzle.Statistics.FillPercentage.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)},");
        
        // Cells array
        sb.AppendLine("  \"cells\": [");
        for (int row = 0; row < grid.Height; row++)
        {
            sb.Append("    [");
            for (int col = 0; col < grid.Width; col++)
            {
                var cell = grid.GetCell(row, col);
                
                if (cell.IsBlocked || cell.HasAsterisk)
                {
                    sb.Append("null");
                }
                else if (cell.HasLetter)
                {
                    sb.Append("{");
                    if (cell.IsNumbered)
                    {
                        sb.Append($"\"num\":{cell.Number},");
                    }
                    sb.Append($"\"letter\":\"{cell.Letter}\"");
                    sb.Append("}");
                }
                else
                {
                    sb.Append("null");
                }
                
                if (col < grid.Width - 1) sb.Append(",");
            }
            sb.Append("]");
            if (row < grid.Height - 1) sb.Append(",");
            sb.AppendLine();
        }
        sb.AppendLine("  ],");
        
        // Clues
        sb.AppendLine("  \"clues\": {");
        
        // Across clues
        sb.AppendLine("    \"across\": [");
        var acrossItems = across.OrderBy(x => GetNumber(x)).ToList();
        for (int i = 0; i < acrossItems.Count; i++)
        {
            var item = acrossItems[i];
            var number = GetNumber(item);
            var clue = GetClueText(item, PrintOptions.Default).Replace("\"", "\\\"");
            var answer = item switch
            {
                Word w => w.Text,
                AccidentalWord a => a.Text,
                _ => ""
            };
            
            sb.Append($"      {{\"number\":{number},\"clue\":\"{clue}\",\"answer\":\"{answer}\"}}");
            if (i < acrossItems.Count - 1) sb.Append(",");
            sb.AppendLine();
        }
        sb.AppendLine("    ],");
        
        // Down clues
        sb.AppendLine("    \"down\": [");
        var downItems = down.OrderBy(x => GetNumber(x)).ToList();
        for (int i = 0; i < downItems.Count; i++)
        {
            var item = downItems[i];
            var number = GetNumber(item);
            var clue = GetClueText(item, PrintOptions.Default).Replace("\"", "\\\"");
            var answer = item switch
            {
                Word w => w.Text,
                AccidentalWord a => a.Text,
                _ => ""
            };
            
            sb.Append($"      {{\"number\":{number},\"clue\":\"{clue}\",\"answer\":\"{answer}\"}}");
            if (i < downItems.Count - 1) sb.Append(",");
            sb.AppendLine();
        }
        sb.AppendLine("    ]");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        
        return sb.ToString();
    }

    /// <summary>
    /// Saves the puzzle as JSON for web display
    /// </summary>
    public async Task SaveAsJsonAsync(CrosswordPuzzle puzzle, string filePath)
    {
        var json = GenerateJsonForWeb(puzzle);
        await File.WriteAllTextAsync(filePath, json, new UTF8Encoding(false));
    }

    private static string CenterText(string text, int width)
    {
        if (text.Length >= width)
            return text;

        var padding = (width - text.Length) / 2;
        return new string(' ', padding) + text;
    }

    /// <summary>
    /// Creates a simple HTML output for web printing
    /// </summary>
    public string GenerateHtmlOutput(CrosswordPuzzle puzzle, PrintOptions options)
    {
        var html = new StringBuilder();
        
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html>");
        html.AppendLine("<head>");
        html.AppendLine("    <meta charset='utf-8'>");
        html.AppendLine("    <title>Svenskt Korsord</title>");
        html.AppendLine("    <style>");
        html.AppendLine(GetCssStyles());
        html.AppendLine("    </style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        
        // Title
        if (options.IncludeTitle)
        {
            html.AppendLine($"    <h1>{options.Title ?? "Svenskt Korsord"}</h1>");
        }
        
        // Grid
        html.AppendLine("    <div class='crossword-grid'>");
        html.AppendLine(CreateHtmlGrid(puzzle.Grid, false));
        html.AppendLine("    </div>");
        
        // Clues
        html.AppendLine("    <div class='clues'>");
        html.AppendLine(CreateHtmlClues(puzzle, options));
        html.AppendLine("    </div>");
        
        html.AppendLine("</body>");
        html.AppendLine("</html>");
        
        return html.ToString();
    }

    private string GetCssStyles()
    {
        return """
        body { font-family: Arial, sans-serif; margin: 20px; }
        h1 { text-align: center; }
        .crossword-grid { margin: 20px 0; }
        .grid-table { border-collapse: collapse; margin: 0 auto; }
        .grid-table td { 
            width: 30px; height: 30px; 
            border: 1px solid #000; 
            text-align: center; 
            vertical-align: middle; 
            font-weight: bold;
        }
        .blocked { background-color: #000; }
        .clues { margin: 20px 0; }
        .clue-section { margin: 15px 0; }
        .clue-section h3 { margin-bottom: 10px; }
        .clue { margin: 3px 0; }
        @media print {
            body { margin: 10px; }
            .grid-table td { width: 25px; height: 25px; }
        }
        """;
    }

    private string CreateHtmlGrid(CrosswordGrid grid, bool showSolution)
    {
        var html = new StringBuilder();
        
        html.AppendLine("        <table class='grid-table'>");
        
        for (int row = 0; row < grid.Height; row++)
        {
            html.AppendLine("            <tr>");
            for (int col = 0; col < grid.Width; col++)
            {
                var cell = grid.GetCell(row, col);
                
                if (cell.IsBlocked)
                {
                    html.AppendLine("                <td class='blocked'></td>");
                }
                else
                {
                    var content = "";
                    if (showSolution && cell.HasLetter)
                    {
                        content = cell.Letter.ToString();
                    }
                    else if (cell.IsNumbered)
                    {
                        content = $"<small>{cell.Number}</small>";
                    }
                    
                    html.AppendLine($"                <td>{content}</td>");
                }
            }
            html.AppendLine("            </tr>");
        }
        
        html.AppendLine("        </table>");
        
        return html.ToString();
    }

    private string CreateHtmlClues(CrosswordPuzzle puzzle, PrintOptions options)
    {
        var (across, down) = GetAllClues(puzzle);
        var html = new StringBuilder();
        
        if (across.Count > 0)
        {
            html.AppendLine("        <div class='clue-section'>");
            html.AppendLine("            <h3>Vågrätt:</h3>");
            foreach (var item in across.OrderBy(x => GetNumber(x)))
            {
                var number = GetNumber(item);
                var clue = GetClueText(item, options);
                html.AppendLine($"            <div class='clue'>{number}. {clue}</div>");
            }
            html.AppendLine("        </div>");
        }

        if (down.Count > 0)
        {
            html.AppendLine("        <div class='clue-section'>");
            html.AppendLine("            <h3>Lodrätt:</h3>");
            foreach (var item in down.OrderBy(x => GetNumber(x)))
            {
                var number = GetNumber(item);
                var clue = GetClueText(item, options);
                html.AppendLine($"            <div class='clue'>{number}. {clue}</div>");
            }
            html.AppendLine("        </div>");
        }
        
        // Add legend if we have bonus words
        var hasAccidentalWords = puzzle.ValidationResult?.ValidAccidentalWords?.Any(w => w.ShouldIncludeInPuzzle) == true;
        if (hasAccidentalWords)
        {
            html.AppendLine("        <div class='legend'>");
            html.AppendLine("            <p><small>? = Bonusord (oavsiktigt giltigt ord)</small></p>");
            html.AppendLine("        </div>");
        }
        
        return html.ToString();
    }

    /// <summary>
    /// Gets all clues including valid accidental words that should be included
    /// </summary>
    private (List<object> Across, List<object> Down) GetAllClues(CrosswordPuzzle puzzle)
    {
        var across = new List<object>();
        var down = new List<object>();
        
        // Add intentional words
        foreach (var word in puzzle.Grid.Words)
        {
            if (word.Direction == Direction.Across)
                across.Add(word);
            else
                down.Add(word);
        }
        
        // Add valid accidental words that should be included as clues
        if (puzzle.ValidationResult?.ValidAccidentalWords != null)
        {
            foreach (var accWord in puzzle.ValidationResult.ValidAccidentalWords.Where(w => w.ShouldIncludeInPuzzle))
            {
                if (accWord.Direction == Direction.Across)
                    across.Add(accWord);
                else
                    down.Add(accWord);
            }
        }
        
        return (across, down);
    }
    
    /// <summary>
    /// Gets the number for either a Word or AccidentalWord
    /// </summary>
    private int GetNumber(object item)
    {
        return item switch
        {
            Word word => word.Number,
            AccidentalWord accWord => accWord.PuzzleNumber,
            _ => 0
        };
    }
    
    /// <summary>
    /// Gets the clue text for either a Word or AccidentalWord
    /// </summary>
    private string GetClueText(object item, PrintOptions options)
    {
        return item switch
        {
            Word word => options.AdjustClues ? 
                _clueGenerator.AdjustClueForDifficulty(word, options.TargetDifficulty ?? word.Difficulty) : 
                word.Clue,
            AccidentalWord accWord => $"{accWord.ClueFromDictionary}", 
            _ => "Okänd ledtråd"
        };
    }
    
    /// <summary>
    /// Gets the word length for either a Word or AccidentalWord
    /// </summary>
    private int GetWordLength(object item)
    {
        return item switch
        {
            Word word => word.Length,
            AccidentalWord accWord => accWord.Length,
            _ => 0
        };
    }
}

/// <summary>
/// Options for controlling print output formatting
/// </summary>
public record PrintOptions
{
    public GridStyle GridStyle { get; init; } = GridStyle.ASCII; // Changed default to ASCII
    public bool IncludeTitle { get; init; } = true;
    public bool IncludeDate { get; init; } = true;
    public bool IncludeStatistics { get; init; } = false;
    public bool IncludeSolution { get; init; } = false;
    public bool IncludeFooter { get; init; } = true;
    public bool ShowWordLength { get; init; } = false;
    public bool AdjustClues { get; init; } = false;
    public string? Title { get; init; }
    public DifficultyLevel? TargetDifficulty { get; init; }

    public static PrintOptions Default => new();
    
    public static PrintOptions PuzzleOnly => new()
    {
        IncludeSolution = false,
        IncludeStatistics = false,
        ShowWordLength = false
    };
    
    public static PrintOptions WithSolution => new()
    {
        IncludeSolution = true,
        IncludeStatistics = true
    };
    
    public static PrintOptions ForPrinter => new()
    {
        GridStyle = GridStyle.ASCII,
        IncludeTitle = true,
        IncludeDate = false,
        IncludeFooter = false,
        ShowWordLength = false
    };
}

public enum GridStyle
{
    ASCII,
    Unicode,
    Simple,
    UnicodeCompat  // Unicode compatible - safer characters
}