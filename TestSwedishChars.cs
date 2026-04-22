using SwedishCrossword.Services;
using System.Text;

namespace TestSwedishChars;

public class Program
{
    public static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        
        Console.WriteLine("???? Swedish Character Encoding Test");
        Console.WriteLine("=====================================");
        
        var dictionary = new SwedishDictionary();
        var allWords = dictionary.AllWords;
        
        Console.WriteLine($"?? Total words loaded: {allWords.Count}");
        
        // Test Swedish character distribution
        var wordsWith� = allWords.Where(w => w.Text.Contains('�') || w.Clue.Contains('�')).ToList();
        var wordsWith� = allWords.Where(w => w.Text.Contains('�') || w.Clue.Contains('�')).ToList();
        var wordsWith� = allWords.Where(w => w.Text.Contains('�') || w.Clue.Contains('�')).ToList();
        
        Console.WriteLine($"\n?? Swedish Characters Distribution:");
        Console.WriteLine($"   Words with �/�: {wordsWith�.Count}");
        Console.WriteLine($"   Words with �/�: {wordsWith�.Count}");
        Console.WriteLine($"   Words with �/�: {wordsWith�.Count}");
        
        // Show sample words
        Console.WriteLine($"\n?? Sample words with �:");
        foreach (var word in wordsWith�.Take(5))
        {
            Console.WriteLine($"   {word.Text} - {word.Clue}");
        }
        
        Console.WriteLine($"\n?? Sample words with �:");
        foreach (var word in wordsWith�.Take(5))
        {
            Console.WriteLine($"   {word.Text} - {word.Clue}");
        }
        
        Console.WriteLine($"\n?? Sample words with �:");
        foreach (var word in wordsWith�.Take(5))
        {
            Console.WriteLine($"   {word.Text} - {word.Clue}");
        }
        
        // Test specific known words
        Console.WriteLine($"\n?? Testing specific Swedish words:");
        
        var testWords = new[] { "JORDGUBBE", "KN�PPAS", "TR�FFA", "D�RR", "F�REM�L" };
        foreach (var testWord in testWords)
        {
            var found = allWords.FirstOrDefault(w => w.Text.Equals(testWord, StringComparison.OrdinalIgnoreCase));
            if (found != null)
            {
                Console.WriteLine($"   ? {found.Text}: {found.Clue}");
            }
            else
            {
                Console.WriteLine($"   ? {testWord}: Not found");
            }
        }
        
        // Character encoding verification
        var totalSwedishChars = allWords.Count(w => 
            w.Text.Contains('�') || w.Text.Contains('�') || w.Text.Contains('�') ||
            w.Clue.Contains('�') || w.Clue.Contains('�') || w.Clue.Contains('�'));
            
        Console.WriteLine($"\n?? Summary:");
        Console.WriteLine($"   Total words: {allWords.Count}");
        Console.WriteLine($"   Words with Swedish characters: {totalSwedishChars}");
        Console.WriteLine($"   Percentage: {(totalSwedishChars * 100.0 / allWords.Count):F1}%");
        
        if (totalSwedishChars > 0)
        {
            Console.WriteLine("\n? SUCCESS: All Swedish characters are properly encoded!");
        }
        else
        {
            Console.WriteLine("\n? ERROR: No Swedish characters found - encoding issue detected!");
        }
        
        // Show categories containing Swedish characters
        var stats = dictionary.GetStatistics();
        Console.WriteLine($"\n?? Top categories:");
        foreach (var category in stats.Categories.OrderByDescending(c => c.Value).Take(10))
        {
            Console.WriteLine($"   {category.Key}: {category.Value} words");
        }
    }
}