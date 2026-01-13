using SwedishCrossword.Models;
using SwedishCrossword.Services;

namespace SwedishCrossword.Tests;

/// <summary>
/// Simple demonstration of the asterisk functionality
/// </summary>
public class AsteriskDemo
{
    public static async Task RunDemo()
    {
        Console.WriteLine("Swedish Crossword Asterisk Demo");
        Console.WriteLine("===============================");
        Console.WriteLine();

        try 
        {
            // Create a simple crossword
            var dictionary = new SwedishDictionary();
            var validator = new GridValidator();
            var generator = new CrosswordGenerator(dictionary, validator);
            var printService = new PrintService(new ClueGenerator());

            var options = CrosswordGenerationOptions.Small; // Use small for quick demo
            
            Console.WriteLine("Generating crossword...");
            var puzzle = await generator.GenerateAsync(options);
            
            Console.WriteLine("Generated crossword with asterisks for empty cells:");
            Console.WriteLine();
            
            // Print the crossword with asterisks
            var printOptions = PrintOptions.Default;
            var output = printService.GeneratePrintableDocument(puzzle, printOptions);
            
            Console.WriteLine(output);
            
            Console.WriteLine("Demo completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during demo: {ex.Message}");
        }
    }
}