using SwedishCrossword.Models;
using SwedishCrossword.Services;

// Demo: Validation during word placement
Console.WriteLine("Swedish Crossword Validation Demo");
Console.WriteLine("=================================");

var dictionary = new SwedishDictionary();
var grid = new CrosswordGrid(10, 10);

Console.WriteLine($"Dictionary loaded with {dictionary.WordCount} words\n");

// Create some test words
var word1 = new Word("KATT", "Fluffig husdjur som jamar");
var word2 = new Word("HUND", "Människans bästa vän");  
var word3 = new Word("XYZ", "Invalid test word"); // This doesn't exist in dictionary

Console.WriteLine("=== Testing Standard Placement ===");
Console.WriteLine("Placing words without validation...");

// Place words normally (without validation)
bool result1 = grid.TryPlaceWord(word1, 3, 3, Direction.Across);
bool result2 = grid.TryPlaceWord(word2, 2, 4, Direction.Down); // Intersects with KATT at 'A'
bool result3 = grid.TryPlaceWord(word3, 5, 3, Direction.Across); // Might create invalid words

Console.WriteLine($"KATT placed: {result1}");
Console.WriteLine($"HUND placed: {result2}");  
Console.WriteLine($"XYZ placed: {result3}");

// Check for accidental words
var validation1 = grid.ValidateCrossword(dictionary);
Console.WriteLine($"\nValidation results:");
Console.WriteLine($"Valid: {validation1.IsValid}");
Console.WriteLine($"Accidental words found: {validation1.AccidentalWords.Count}");
Console.WriteLine($"Invalid accidental words: {validation1.InvalidAccidentalWords.Count}");

if (validation1.InvalidAccidentalWords.Any())
{
    Console.WriteLine("Invalid words detected:");
    foreach (var word in validation1.InvalidAccidentalWords)
    {
        Console.WriteLine($"  - {word.Text} at ({word.StartRow}, {word.StartCol})");
    }
}

Console.WriteLine("\n=== Testing Validation-Aware Placement ===");
Console.WriteLine("Creating new grid with validation during placement...");

// Create a new grid to test validation-aware placement
var validatedGrid = new CrosswordGrid(10, 10);

Console.WriteLine("Placing words with validation...");
bool validResult1 = validatedGrid.TryPlaceWordWithValidation(word1, 3, 3, Direction.Across, dictionary, rejectInvalidWords: true);
bool validResult2 = validatedGrid.TryPlaceWordWithValidation(word2, 2, 4, Direction.Down, dictionary, rejectInvalidWords: true);

// Create a word that would definitely create invalid accidental words
var problematicWord = new Word("QZZX", "Made up word");
bool validResult3 = validatedGrid.TryPlaceWordWithValidation(problematicWord, 5, 3, Direction.Across, dictionary, rejectInvalidWords: true);

Console.WriteLine($"KATT placed with validation: {validResult1}");
Console.WriteLine($"HUND placed with validation: {validResult2}");  
Console.WriteLine($"QZZX placed with validation: {validResult3}"); // Should be rejected

// Final validation
var validation2 = validatedGrid.ValidateCrossword(dictionary);
Console.WriteLine($"\nFinal validation results:");
Console.WriteLine($"Valid: {validation2.IsValid}");
Console.WriteLine($"Accidental words found: {validation2.AccidentalWords.Count}");
Console.WriteLine($"Invalid accidental words: {validation2.InvalidAccidentalWords.Count}");

Console.WriteLine("\n=== Summary ===");
Console.WriteLine("Standard placement: Places words first, validates after");
Console.WriteLine("Validation-aware placement: Validates during placement, rejects invalid combinations");
Console.WriteLine("This prevents crosswords with invalid accidental words from being created!");