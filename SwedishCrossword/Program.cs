using System.Text;
using SwedishCrossword.Models;
using SwedishCrossword.Services;

namespace SwedishCrossword;

internal class Program
{
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("🇸🇪 Svenskt Korsord Generator");
        Console.WriteLine("============================");

        try
        {
            // Initialize services
            var dictionary = new SwedishDictionary();
            var validator = new GridValidator();
            var generator = new CrosswordGenerator(dictionary, validator);
            var clueGenerator = new ClueGenerator();
            var printService = new PrintService(clueGenerator);

            Console.WriteLine($"Ordlista laddad: {dictionary.WordCount:N0} ord");
            Console.WriteLine();

            // Show menu
            while (true)
            {
                Console.WriteLine("Välj alternativ:");
                Console.WriteLine("1. Generera enkelt korsord (11x11) - alla svårighetsgrader");
                Console.WriteLine("2. Generera medel korsord (15x15) - alla svårighetsgrader");
                Console.WriteLine("3. Generera svårt korsord (19x19) - alla svårighetsgrader");
                Console.WriteLine("4. Visa ordlistestatistik");
                Console.WriteLine("5. Importera ord från Lexin (ISOF)");
                Console.WriteLine("6. Generera korsord för webben");
                Console.WriteLine("0. Avsluta");
                Console.WriteLine();
                Console.Write("Ditt val: ");

                var choice = Console.ReadLine();
                Console.WriteLine();

                try
                {
                    switch (choice)
                    {
                        case "1":
                            await GeneratePuzzle(generator, printService, CrosswordGenerationOptions.Easy, "Enkelt");
                            break;

                        case "2":
                            await GeneratePuzzle(generator, printService, CrosswordGenerationOptions.Medium, "Medel");
                            break;

                        case "3":
                            await GeneratePuzzle(generator, printService, CrosswordGenerationOptions.Hard, "Svårt");
                            break;

                        case "4":
                            ShowDictionaryStats(dictionary);
                            break;

                        case "5":
                            await ImportFromLexin();
                            break;

                        case "6":
                            await GenerateForWeb(generator, printService, CrosswordGenerationOptions.Hard);
                            break;

                        case "0":
                            Console.WriteLine("Tack för att du använde Svenskt Korsord Generator!");
                            return;

                        default:
                            Console.WriteLine("Ogiltigt val. Försök igen.");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Fel: {ex.Message}");
                    Console.WriteLine("Försöker igen...");
                }

                Console.WriteLine();
                Console.WriteLine("Tryck på valfri tangent för att fortsätta...");
                Console.ReadKey();
                Console.Clear();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Kritiskt fel: {ex.Message}");
            Console.WriteLine("Programmet avslutas.");
        }
    }

    private static async Task GeneratePuzzle(
        CrosswordGenerator generator, 
        PrintService printService, 
        CrosswordGenerationOptions options,
        string difficulty)
    {
        Console.WriteLine($"🎯 Genererar {difficulty.ToLower()} korsord ({options.Width}x{options.Height})...");
        Console.WriteLine("Detta kan ta en stund...");
        Console.WriteLine();

        var startTime = DateTime.Now;
        var puzzle = await generator.GenerateAsync(options);
        var duration = DateTime.Now - startTime;

        Console.WriteLine("✅ Korsord genererat!");
        Console.WriteLine($"⏱️  Tid: {duration.TotalSeconds:F1} sekunder");
        Console.WriteLine($"🔄 Försök: {puzzle.GenerationAttempts:N0}");
        Console.WriteLine($"📊 Fyllnadsgrad: {puzzle.Statistics.FillPercentage:F1}%");
        Console.WriteLine($"📝 Ord: {puzzle.Statistics.WordCount}");
        Console.WriteLine();

        // Print the puzzle
        var printOptions = PrintOptions.Default;
        var output = printService.GeneratePrintableDocument(puzzle, printOptions);
        Console.WriteLine(output);

        // Ask if user wants to save
        Console.Write("Vill du spara korsordet till fil? (j/n): ");
        if (Console.ReadLine()?.ToLower() == "j")
        {
            var fileName = $"korsord-{difficulty.ToLower()}-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
            await printService.SaveToFileAsync(puzzle, fileName, printOptions);
            Console.WriteLine($"💾 Sparat som: {fileName}");
        }
    }

    private static async Task GenerateForWeb(
        CrosswordGenerator generator,
        PrintService printService,
        CrosswordGenerationOptions options)
    {
        Console.WriteLine("🌐 Genererar korsord för webben...");
        Console.WriteLine();

        var puzzle = await generator.GenerateAsync(options);

        Console.WriteLine("✅ Korsord genererat!");
        Console.WriteLine($"📊 Fyllnadsgrad: {puzzle.Statistics.FillPercentage:F1}%");
        Console.WriteLine($"📝 Ord: {puzzle.Statistics.WordCount}");
        Console.WriteLine();

        // Ensure wwwroot directory exists
        var wwwrootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (!Directory.Exists(wwwrootPath))
        {
            // Try relative path from project
            wwwrootPath = "wwwroot";
        }
        
        if (!Directory.Exists(wwwrootPath))
        {
            Directory.CreateDirectory(wwwrootPath);
        }

        // Save JSON data
        var jsonPath = Path.Combine(wwwrootPath, "puzzle.json");
        await printService.SaveAsJsonAsync(puzzle, jsonPath);
        Console.WriteLine($"💾 JSON sparad: {jsonPath}");

        // Also save HTML with embedded data
        var htmlPath = Path.Combine(wwwrootPath, "puzzle.html");
        var html = GenerateStandaloneHtml(puzzle, printService);
        await File.WriteAllTextAsync(htmlPath, html, new UTF8Encoding(false));
        Console.WriteLine($"🌐 HTML sparad: {htmlPath}");

        Console.WriteLine();
        Console.WriteLine("För att spela korsordet, öppna filen i en webbläsare:");
        Console.WriteLine($"   file:///{Path.GetFullPath(htmlPath).Replace('\\', '/')}");
    }

    private static string GenerateStandaloneHtml(CrosswordPuzzle puzzle, PrintService printService)
    {
        var json = printService.GenerateJsonForWeb(puzzle);
        
        // Read the template HTML and inject the puzzle data
        var html = GetWebTemplate();
        
        // Replace the sample puzzleData with the real data
        var dataPlaceholder = "const puzzleData = {";
        var dataEndMarker = "};";
        
        var startIndex = html.IndexOf(dataPlaceholder);
        if (startIndex >= 0)
        {
            var endIndex = html.IndexOf(dataEndMarker, startIndex);
            if (endIndex >= 0)
            {
                endIndex += dataEndMarker.Length;
                html = html[..startIndex] + "const puzzleData = " + json + ";" + html[(endIndex)..];
            }
        }
        
        return html;
    }

    private static string GetWebTemplate()
    {
        // Return the HTML template
        return """
<!DOCTYPE html>
<html lang="sv">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Svenskt Korsord</title>
    <style>
        :root { --cell-size: 36px; --border-color: #333; --blocked-color: #1a1a1a; --cell-bg: #fff; --number-color: #666; --letter-color: #000; --highlight-color: #fffacd; --accent-color: #2563eb; --word-highlight: #e0f2fe; }
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body { font-family: 'Segoe UI', system-ui, sans-serif; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); min-height: 100vh; padding: 20px; }
        .container { max-width: 1600px; margin: 0 auto; }
        header { text-align: center; color: white; margin-bottom: 15px; }
        header h1 { font-size: 2rem; margin-bottom: 6px; text-shadow: 2px 2px 4px rgba(0,0,0,0.3); }
        header p { font-size: 0.9rem; opacity: 0.9; }
        .main-layout { display: flex; gap: 20px; justify-content: center; align-items: flex-start; }
        .grid-section { background: white; border-radius: 12px; padding: 15px; box-shadow: 0 10px 40px rgba(0,0,0,0.2); flex-shrink: 0; }
        .grid-section h2 { text-align: center; margin-bottom: 10px; color: #333; font-size: 1.1rem; }
        .crossword-grid { display: inline-block; border: 3px solid var(--border-color); background: var(--border-color); }
        .grid-row { display: flex; }
        .cell { width: var(--cell-size); height: var(--cell-size); background: var(--cell-bg); border: 1px solid #ccc; position: relative; display: flex; align-items: center; justify-content: center; font-size: 1.2rem; font-weight: bold; cursor: pointer; transition: background-color 0.15s; }
        .cell:hover:not(.blocked) { background: var(--highlight-color); }
        .cell.blocked { background: var(--blocked-color); cursor: default; }
        .cell.word-highlight { background: var(--word-highlight); }
        .cell .number { position: absolute; top: 1px; left: 2px; font-size: 0.55rem; font-weight: normal; color: var(--number-color); }
        .cell input { width: 100%; height: 100%; border: none; background: transparent; text-align: center; font-size: 1.2rem; font-weight: bold; text-transform: uppercase; outline: none; padding-top: 6px; }
        .cell input:focus { background: var(--highlight-color); }
        .cell.correct input { color: #16a34a; }
        .cell.incorrect input { color: #dc2626; background: #fef2f2; }
        .cell.empty-warning { background: #fef3c7; }
        .controls { display: flex; gap: 6px; margin-top: 12px; justify-content: center; flex-wrap: wrap; }
        .btn { padding: 6px 12px; border: none; border-radius: 6px; font-size: 0.8rem; font-weight: 600; cursor: pointer; }
        .btn-primary { background: var(--accent-color); color: white; }
        .btn-secondary { background: #e5e7eb; color: #374151; }
        .btn-success { background: #16a34a; color: white; }
        .stats { text-align: center; margin-top: 8px; color: #666; font-size: 0.8rem; }
        .timer { font-size: 1.2rem; font-weight: bold; color: var(--accent-color); text-align: center; margin-bottom: 10px; }
        .clues-section { background: white; border-radius: 12px; padding: 15px; box-shadow: 0 10px 40px rgba(0,0,0,0.2); min-width: 450px; max-width: 550px; display: flex; flex-direction: column; overflow: hidden; }
        .clues-section h2 { color: #333; font-size: 1.1rem; margin-bottom: 10px; padding-bottom: 6px; border-bottom: 2px solid var(--accent-color); flex-shrink: 0; }
        .clues-columns { display: flex; gap: 15px; flex: 1; overflow: hidden; min-height: 0; }
        .clue-column { flex: 1; min-width: 0; display: flex; flex-direction: column; overflow: hidden; min-height: 0; }
        .clue-direction { display: flex; flex-direction: column; flex: 1; overflow: hidden; min-height: 0; }
        .clue-direction h3 { color: var(--accent-color); font-size: 0.9rem; margin-bottom: 6px; padding-bottom: 4px; border-bottom: 1px solid #e5e7eb; flex-shrink: 0; }
        .clue-list { list-style: none; flex: 1; overflow-y: auto; min-height: 0; padding-right: 5px; }
        .clue-list::-webkit-scrollbar { width: 6px; }
        .clue-list::-webkit-scrollbar-track { background: #f1f1f1; border-radius: 3px; }
        .clue-list::-webkit-scrollbar-thumb { background: #c1c1c1; border-radius: 3px; }
        .clue-item { padding: 6px 8px; margin-bottom: 4px; border-radius: 4px; cursor: pointer; font-size: 0.85rem; line-height: 1.3; }
        .clue-item:hover { background: #f3f4f6; }
        .clue-item.active { background: var(--word-highlight); border-left: 3px solid var(--accent-color); }
        .clue-number { font-weight: bold; color: var(--accent-color); margin-right: 6px; }
        @media (max-width: 1100px) { .main-layout { flex-direction: column; align-items: center; } .clues-section { min-width: auto; max-width: 100%; width: 100%; max-height: none !important; } }
        @media (max-width: 600px) { :root { --cell-size: 28px; } .clues-columns { flex-direction: column; } }
    </style>
</head>
<body>
    <div class="container">
        <header><h1>🇸🇪 Svenskt Korsord</h1><p>Klicka på en ruta och skriv • Mellanslag byter riktning</p></header>
        <div class="main-layout">
            <div class="grid-section">
                <h2>Korsord</h2>
                <div class="timer" id="timer">00:00</div>
                <div class="crossword-grid" id="crossword-grid"></div>
                <div class="controls">
                    <button class="btn btn-primary" onclick="checkAnswers()">✓ Kontrollera</button>
                    <button class="btn btn-secondary" onclick="clearGrid()">🗑️ Rensa</button>
                    <button class="btn btn-success" onclick="showSolution()">👁️ Visa lösning</button>
                </div>
                <div class="stats" id="stats"></div>
            </div>
            <div class="clues-section">
                <h2>Ledtrådar</h2>
                <div class="clues-columns">
                    <div class="clue-column"><div class="clue-direction"><h3>Vågrätt →</h3><ul class="clue-list" id="across-clues"></ul></div></div>
                    <div class="clue-column"><div class="clue-direction"><h3>Lodrätt ↓</h3><ul class="clue-list" id="down-clues"></ul></div></div>
                </div>
            </div>
        </div>
    </div>
    <script>
        const puzzleData = {};
        let timerInterval, seconds = 0, puzzleSolved = false, currentDirection = 'across';
        function init() { renderGrid(); renderClues(); syncCluesHeight(); startTimer(); updateStats(); window.addEventListener('resize', syncCluesHeight); }
        function syncCluesHeight() { const g = document.querySelector('.grid-section'), c = document.querySelector('.clues-section'); if (g && c) c.style.maxHeight = g.offsetHeight + 'px'; }
        function renderGrid() {
            const grid = document.getElementById('crossword-grid'); grid.innerHTML = '';
            for (let row = 0; row < puzzleData.height; row++) {
                const rowDiv = document.createElement('div'); rowDiv.className = 'grid-row';
                for (let col = 0; col < puzzleData.width; col++) {
                    const cellData = puzzleData.cells[row]?.[col];
                    const cellDiv = document.createElement('div'); cellDiv.className = 'cell'; cellDiv.dataset.row = row; cellDiv.dataset.col = col;
                    if (cellData === null) { cellDiv.classList.add('blocked'); }
                    else {
                        if (cellData.num) { const n = document.createElement('span'); n.className = 'number'; n.textContent = cellData.num; cellDiv.appendChild(n); }
                        const input = document.createElement('input'); input.type = 'text'; input.maxLength = 1; input.dataset.answer = cellData.letter;
                        input.addEventListener('input', handleInput); input.addEventListener('keydown', handleKeyDown); input.addEventListener('focus', () => handleFocus(row, col));
                        cellDiv.appendChild(input);
                    }
                    rowDiv.appendChild(cellDiv);
                }
                grid.appendChild(rowDiv);
            }
        }
        function renderClues() {
            const ac = document.getElementById('across-clues'), dc = document.getElementById('down-clues'); ac.innerHTML = ''; dc.innerHTML = '';
            (puzzleData.clues.across||[]).forEach(c => { const li = document.createElement('li'); li.className = 'clue-item'; li.innerHTML = `<span class="clue-number">${c.number}.</span>${c.clue}`; li.dataset.number = c.number; li.dataset.direction = 'across'; li.onclick = () => focusClue(c.number, 'across'); ac.appendChild(li); });
            (puzzleData.clues.down||[]).forEach(c => { const li = document.createElement('li'); li.className = 'clue-item'; li.innerHTML = `<span class="clue-number">${c.number}.</span>${c.clue}`; li.dataset.number = c.number; li.dataset.direction = 'down'; li.onclick = () => focusClue(c.number, 'down'); dc.appendChild(li); });
        }
        function handleInput(e) { e.target.value = e.target.value.toUpperCase(); e.target.parentElement.classList.remove('empty-warning'); if (e.target.value) moveInDirection(e.target); updateStats(); }
        function handleKeyDown(e) {
            const cell = e.target.parentElement, row = parseInt(cell.dataset.row), col = parseInt(cell.dataset.col);
            switch(e.key) {
                case 'ArrowRight': currentDirection = 'across'; moveTo(row, col+1); e.preventDefault(); break;
                case 'ArrowLeft': currentDirection = 'across'; moveTo(row, col-1); e.preventDefault(); break;
                case 'ArrowDown': currentDirection = 'down'; moveTo(row+1, col); e.preventDefault(); break;
                case 'ArrowUp': currentDirection = 'down'; moveTo(row-1, col); e.preventDefault(); break;
                case 'Backspace': if (!e.target.value) { moveBackInDirection(e.target); e.preventDefault(); } break;
                case ' ': currentDirection = currentDirection === 'across' ? 'down' : 'across'; handleFocus(row, col); e.preventDefault(); break;
            }
        }
        function handleFocus(row, col) { highlightWord(row, col); highlightClue(row, col); }
        function highlightWord(row, col) {
            document.querySelectorAll('.cell.word-highlight').forEach(c => c.classList.remove('word-highlight'));
            if (currentDirection === 'across') { let s = col; while (s > 0 && puzzleData.cells[row]?.[s-1] !== null) s--; for (let c = s; c < puzzleData.width && puzzleData.cells[row]?.[c] !== null; c++) document.querySelector(`.cell[data-row="${row}"][data-col="${c}"]`)?.classList.add('word-highlight'); }
            else { let s = row; while (s > 0 && puzzleData.cells[s-1]?.[col] !== null) s--; for (let r = s; r < puzzleData.height && puzzleData.cells[r]?.[col] !== null; r++) document.querySelector(`.cell[data-row="${r}"][data-col="${col}"]`)?.classList.add('word-highlight'); }
        }
        function moveTo(row, col) { if (row < 0 || row >= puzzleData.height || col < 0 || col >= puzzleData.width || puzzleData.cells[row]?.[col] === null) return false; const c = document.querySelector(`.cell[data-row="${row}"][data-col="${col}"]`); if (c && !c.classList.contains('blocked')) { c.querySelector('input')?.focus(); return true; } return false; }
        function moveInDirection(input) { const c = input.parentElement, row = parseInt(c.dataset.row), col = parseInt(c.dataset.col); currentDirection === 'across' ? moveTo(row, col+1) : moveTo(row+1, col); }
        function moveBackInDirection(input) { const c = input.parentElement, row = parseInt(c.dataset.row), col = parseInt(c.dataset.col); currentDirection === 'across' ? moveTo(row, col-1) : moveTo(row-1, col); }
        function focusClue(num, dir) { currentDirection = dir; for (let r = 0; r < puzzleData.height; r++) for (let c = 0; c < puzzleData.width; c++) if (puzzleData.cells[r]?.[c]?.num === num) { moveTo(r, c); return; } }
        function highlightClue(row, col) { document.querySelectorAll('.clue-item').forEach(i => i.classList.remove('active')); const d = puzzleData.cells[row]?.[col]; if (d?.num) { const i = document.querySelector(`.clue-item[data-number="${d.num}"][data-direction="${currentDirection}"]`); if (i) { i.classList.add('active'); i.scrollIntoView({ behavior: 'smooth', block: 'nearest' }); } } }
        function checkAnswers() {
            const inputs = document.querySelectorAll('.cell:not(.blocked) input'); let correct = 0, total = inputs.length, filled = 0;
            inputs.forEach(input => { const cell = input.parentElement; cell.classList.remove('correct', 'incorrect', 'empty-warning'); const v = input.value.toUpperCase(); if (v) { filled++; if (v === input.dataset.answer) { correct++; cell.classList.add('correct'); } else { cell.classList.add('incorrect'); } } else { cell.classList.add('empty-warning'); } });
            if (filled === total && correct === total) { puzzleSolved = true; stopTimer(); inputs.forEach(i => i.parentElement.classList.remove('empty-warning')); setTimeout(() => alert(`🎉 Grattis! Du löste korsordet på ${formatTime(seconds)}!`), 100); }
            else if (filled < total) { alert(`Du har ${total - filled} tomma rutor kvar.\n\n${correct} av ${filled} ifyllda är korrekta.`); }
            else { alert(`${filled - correct} bokstäver är felaktiga. Försök igen!`); }
        }
        function clearGrid() { if (confirm('Rensa alla svar?')) { document.querySelectorAll('.cell:not(.blocked) input').forEach(i => { i.value = ''; i.parentElement.classList.remove('correct', 'incorrect', 'empty-warning'); }); updateStats(); } }
        function showSolution() { if (confirm('Visa lösningen?')) { document.querySelectorAll('.cell:not(.blocked) input').forEach(i => { i.value = i.dataset.answer; i.parentElement.classList.remove('empty-warning', 'incorrect'); i.parentElement.classList.add('correct'); }); puzzleSolved = true; stopTimer(); updateStats(); } }
        function startTimer() { timerInterval = setInterval(() => { if (!puzzleSolved) { seconds++; document.getElementById('timer').textContent = formatTime(seconds); } }, 1000); }
        function stopTimer() { clearInterval(timerInterval); }
        function formatTime(s) { return `${Math.floor(s/60).toString().padStart(2,'0')}:${(s%60).toString().padStart(2,'0')}`; }
        function updateStats() { const inputs = document.querySelectorAll('.cell:not(.blocked) input'); const f = Array.from(inputs).filter(i => i.value).length; document.getElementById('stats').textContent = `${f}/${inputs.length} rutor (${Math.round(f/inputs.length*100)}%)`; }
        document.addEventListener('DOMContentLoaded', init);
    </script>
</body>
</html>
""";
    }

    private static void ShowDictionaryStats(SwedishDictionary dictionary)
    {
        if (dictionary.WordCount == 0)
        {
            Console.WriteLine("📊 Ordlistestatistik");
            Console.WriteLine("==================");
            Console.WriteLine();
            Console.WriteLine("❌ Ordlistan är tom!");
            Console.WriteLine();
            Console.WriteLine("För att ladda ord, välj alternativ 5 'Importera ord från Lexin (ISOF)'");
            Console.WriteLine($"Förväntad sökväg: {LexinWordImporter.GetJsonFilePath()}");
            return;
        }
        
        var stats = dictionary.GetStatistics();
        
        Console.WriteLine("📊 Ordlistestatistik");
        Console.WriteLine("==================");
        Console.WriteLine($"Totalt antal ord: {stats.TotalWords:N0}");
        Console.WriteLine($"Kategorier: {stats.Categories.Count}");
        Console.WriteLine($"Genomsnittlig längd: {stats.AverageLength:F1} bokstäver");
        Console.WriteLine($"Längdspann: {stats.MinLength}-{stats.MaxLength} bokstäver");
        Console.WriteLine($"Datakälla: {LexinWordImporter.GetJsonFilePath()}");
        Console.WriteLine();

        Console.WriteLine("📈 Fördelning per svårighetsgrad:");
        foreach (var difficulty in stats.DifficultyDistribution.OrderBy(d => d.Key))
        {
            Console.WriteLine($"  {difficulty.Key}: {difficulty.Value:N0} ord");
        }
        Console.WriteLine();

        Console.WriteLine("🏷️  Största kategorier:");
        foreach (var category in stats.Categories.OrderByDescending(c => c.Value).Take(10))
        {
            Console.WriteLine($"  {category.Key}: {category.Value:N0} ord");
        }
        Console.WriteLine();

        Console.WriteLine("📏 Fördelning per längd:");
        foreach (var length in stats.LengthDistribution.OrderBy(l => l.Key))
        {
            var bar = new string('█', Math.Min(50, length.Value / 50 + 1));
            Console.WriteLine($"  {length.Key,2} bokstäver: {length.Value,5:N0} ord {bar}");
        }
    }

    private static async Task ImportFromLexin()
    {
        Console.WriteLine("🔄 Lexin Import (ISOF Svenska Ordbok)");
        Console.WriteLine("=====================================");
        Console.WriteLine();
        Console.WriteLine("Detta kommer att:");
        Console.WriteLine("  1. Ladda ner Lexin XML-filen (28 MB) om den inte finns");
        Console.WriteLine("  2. Parsa XML och extrahera ord med definitioner");
        Console.WriteLine("  3. Exportera till JSON för snabb laddning");
        Console.WriteLine();
        Console.Write("Vill du fortsätta? (j/n): ");

        if (Console.ReadLine()?.ToLower() != "j")
        {
            Console.WriteLine("Import avbruten.");
            return;
        }

        Console.WriteLine();

        var importer = new LexinWordImporter();
        
        try
        {
            var words = await importer.ImportAndExportAsync();
            
            Console.WriteLine();
            LexinWordImporter.PrintStatistics(words);
            
            Console.WriteLine();
            Console.WriteLine("✅ Import klar!");
            Console.WriteLine("   Starta om programmet för att använda de nya orden.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Import misslyckades: {ex.Message}");
            
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Detaljer: {ex.InnerException.Message}");
            }
        }
    }
}
