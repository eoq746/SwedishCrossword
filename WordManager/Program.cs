using System.Text.Json;
using SwedishCrossword.Services;

namespace WordManager;

public class Program 
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== SWEDISH WORD MANAGER ===");
        Console.WriteLine("1. Add words quickly");
        Console.WriteLine("2. Convert text to JSON");
        Console.WriteLine("3. Merge word files");
        
        var choice = Console.ReadKey(true).KeyChar;
        
        switch (choice)
        {
            case '1':
                await AddWordsQuickly();
                break;
            case '2':
                await ConvertTextToJson();
                break;
            case '3':
                await MergeWordFiles();
                break;
        }
    }
    
    private static async Task AddWordsQuickly()
    {
        Console.WriteLine("Enter words in format: WORD|Clue|Category|Difficulty");
        Console.WriteLine("Enter empty line to finish");
        
        var words = new List<WordEntry>();
        
        while (true)
        {
            Console.Write("Word: ");
            var input = Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(input)) break;
            
            var parts = input.Split('|');
            if (parts.Length == 4)
            {
                words.Add(new WordEntry 
                {
                    Word = parts[0].Trim().ToUpperInvariant(),
                    Clue = parts[1].Trim(),
                    Category = parts[2].Trim(),
                    Difficulty = parts[3].Trim()
                });
                Console.WriteLine($"Added: {parts[0]}");
            }
        }
        
        if (words.Any())
        {
            var fileName = $"new-words-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            var json = JsonSerializer.Serialize(words, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(fileName, json);
            Console.WriteLine($"Saved {words.Count} words to {fileName}");
        }
    }
    
    // ... other methods
}