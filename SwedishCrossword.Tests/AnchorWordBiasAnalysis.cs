using TUnit.Core;
using SwedishCrossword.Models;
using SwedishCrossword.Services;
using System.Diagnostics;

namespace SwedishCrossword.Tests;

/// <summary>
/// Tests to analyze anchor word selection bias and its impact on fill percentage
/// </summary>
public class AnchorWordBiasAnalysis
{
    [Test]
    public async Task Analyze_Anchor_Word_Selection_Patterns()
    {
        // Arrange
        var dictionary = new SwedishDictionary();
        
        if (dictionary.WordCount == 0)
        {
            Console.WriteLine("SKIPPED: No words in dictionary.");
            return;
        }
        
        Console.WriteLine($"Dictionary has {dictionary.WordCount} words");
        Console.WriteLine();
        
        // Analyze what words would be selected as anchors
        var options = CrosswordGenerationOptions.Easy;
        var candidateWords = dictionary.GetWords(
            minLength: options.MinWordLength,
            maxLength: options.MaxWordLength
        ).ToList();
        
        Console.WriteLine($"Candidate words for Easy ({options.Width}x{options.Height}): {candidateWords.Count}");
        
        // Score words using the same logic as the generator
        var scoredWords = candidateWords
            .Where(w => w.Length >= 5 && w.Length <= Math.Min(10, options.Width - 2))
            .Select(w => new
            {
                Word = w,
                VowelScore = w.Text.Count(c => "AEIOU".Contains(c)) * 3,
                ConsonantScore = w.Text.Count(c => "RNSTL".Contains(c)) * 2,
                SwedishScore = w.Text.Count(c => "ÅÄÖ".Contains(c)) * 1,
                LengthBonus = (w.Length >= 5 && w.Length <= 8) ? 5 : 0,
                UniqueLetterBonus = w.Text.Distinct().Count() * 0.5,
                TotalScore = w.Text.Count(c => "AEIOU".Contains(c)) * 3 +
                            w.Text.Count(c => "RNSTL".Contains(c)) * 2 +
                            w.Text.Count(c => "ÅÄÖ".Contains(c)) * 1 +
                            ((w.Length >= 5 && w.Length <= 8) ? 5 : 0) +
                            w.Text.Distinct().Count() * 0.5
            })
            .OrderByDescending(w => w.TotalScore)
            .ToList();
        
        Console.WriteLine();
        Console.WriteLine("=== TOP 20 ANCHOR WORD CANDIDATES ===");
        Console.WriteLine("Word\t\tLen\tVowels\tConson\tSwed\tUnique\tTotal");
        Console.WriteLine(new string('-', 70));
        
        foreach (var w in scoredWords.Take(20))
        {
            var wordPadded = w.Word.Text.PadRight(12);
            Console.WriteLine($"{wordPadded}\t{w.Word.Length}\t{w.VowelScore}\t{w.ConsonantScore}\t{w.SwedishScore}\t{w.UniqueLetterBonus:F1}\t{w.TotalScore:F1}");
        }
        
        // Check for patterns in top words
        Console.WriteLine();
        Console.WriteLine("=== PATTERN ANALYSIS ===");
        
        var top50 = scoredWords.Take(50).ToList();
        var avgLength = top50.Average(w => w.Word.Length);
        var avgVowels = top50.Average(w => w.VowelScore / 3.0);
        var lengthDistribution = top50.GroupBy(w => w.Word.Length)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key}:{g.Count()}");
        
        Console.WriteLine($"Average length of top 50: {avgLength:F1}");
        Console.WriteLine($"Average vowel count: {avgVowels:F1}");
        Console.WriteLine($"Length distribution: {string.Join(", ", lengthDistribution)}");
        
        // Check if top words have rare letters that limit intersections
        var rareLetter = "QXZWJ";
        var topWithRareLetters = top50.Count(w => w.Word.Text.Any(c => rareLetter.Contains(c)));
        Console.WriteLine($"Top 50 with rare letters (Q,X,Z,W,J): {topWithRareLetters}");
        
        await Assert.That(scoredWords.Count).IsGreaterThan(0);
    }
    
    [Test]
    public async Task Compare_Fill_With_Different_Anchor_Strategies()
    {
        var dictionary = new SwedishDictionary();
        var validator = new GridValidator();
        
        if (dictionary.WordCount == 0)
        {
            Console.WriteLine("SKIPPED: No words in dictionary.");
            return;
        }
        
        var options = CrosswordGenerationOptions.Easy;
        const int iterations = 3;
        
        Console.WriteLine("=== CURRENT STRATEGY (vowel-heavy anchors) ===");
        var generator = new CrosswordGenerator(dictionary, validator);
        var currentResults = new List<double>();
        
        for (int i = 0; i < iterations; i++)
        {
            var puzzle = await generator.GenerateAsync(options);
            currentResults.Add(puzzle.Statistics.FillPercentage);
            Console.WriteLine($"  Run {i + 1}: {puzzle.Statistics.FillPercentage:F1}% fill, {puzzle.Statistics.WordCount} words");
        }
        
        Console.WriteLine($"  Average: {currentResults.Average():F1}%");
        Console.WriteLine();
        
        // Analyze what the first anchor word was in each case
        Console.WriteLine("The current strategy favors words with:");
        Console.WriteLine("  - Many vowels (AEIOU) - 3x weight");
        Console.WriteLine("  - Common consonants (RNSTL) - 2x weight");
        Console.WriteLine("  - Swedish chars (ÅÄÖ) - 1x weight");
        Console.WriteLine("  - Length 5-8 - bonus");
        Console.WriteLine();
        
        await Assert.That(currentResults.Average()).IsGreaterThanOrEqualTo(options.TargetFillPercentage);
    }
    
    [Test]
    public async Task Analyze_Intersection_Potential_Of_Top_Anchors()
    {
        var dictionary = new SwedishDictionary();
        
        if (dictionary.WordCount == 0)
        {
            Console.WriteLine("SKIPPED: No words in dictionary.");
            return;
        }
        
        var options = CrosswordGenerationOptions.Easy;
        var allWords = dictionary.GetWords(
            minLength: options.MinWordLength,
            maxLength: options.MaxWordLength
        ).ToList();
        
        // Get top anchor candidates
        var anchorCandidates = allWords
            .Where(w => w.Length >= 5 && w.Length <= Math.Min(10, options.Width - 2))
            .Select(w => new
            {
                Word = w,
                AnchorScore = w.Text.Count(c => "AEIOU".Contains(c)) * 3 +
                             w.Text.Count(c => "RNSTL".Contains(c)) * 2 +
                             w.Text.Count(c => "ÅÄÖ".Contains(c)) * 1 +
                             ((w.Length >= 5 && w.Length <= 8) ? 5 : 0) +
                             w.Text.Distinct().Count() * 0.5
            })
            .OrderByDescending(w => w.AnchorScore)
            .Take(10)
            .ToList();
        
        Console.WriteLine("=== INTERSECTION POTENTIAL ANALYSIS ===");
        Console.WriteLine();
        
        foreach (var anchor in anchorCandidates)
        {
            // Count how many other words could intersect with this anchor
            var intersectionCount = 0;
            var letterIntersections = new Dictionary<char, int>();
            
            foreach (var letter in anchor.Word.Text.Distinct())
            {
                var wordsWithLetter = allWords.Count(w => w != anchor.Word && w.Text.Contains(letter));
                letterIntersections[letter] = wordsWithLetter;
                intersectionCount += wordsWithLetter;
            }
            
            var avgIntersectionsPerLetter = intersectionCount / (double)anchor.Word.Text.Distinct().Count();
            
            Console.WriteLine($"{anchor.Word.Text} (score: {anchor.AnchorScore:F1})");
            Console.WriteLine($"  Total intersection opportunities: {intersectionCount}");
            Console.WriteLine($"  Avg per unique letter: {avgIntersectionsPerLetter:F0}");
            
            // Show letter breakdown
            var letterBreakdown = letterIntersections
                .OrderByDescending(kv => kv.Value)
                .Take(5)
                .Select(kv => $"{kv.Key}:{kv.Value}");
            Console.WriteLine($"  Top letters: {string.Join(", ", letterBreakdown)}");
            Console.WriteLine();
        }
        
        await Assert.That(anchorCandidates.Count).IsGreaterThan(0);
    }
    
    [Test]
    public async Task Test_Alternative_Anchor_Selection_By_Intersection_Potential()
    {
        var dictionary = new SwedishDictionary();
        
        if (dictionary.WordCount == 0)
        {
            Console.WriteLine("SKIPPED: No words in dictionary.");
            return;
        }
        
        var options = CrosswordGenerationOptions.Easy;
        var allWords = dictionary.GetWords(
            minLength: options.MinWordLength,
            maxLength: options.MaxWordLength
        ).ToList();
        
        Console.WriteLine("=== ALTERNATIVE: RANK BY ACTUAL INTERSECTION POTENTIAL ===");
        Console.WriteLine();
        
        // Alternative scoring: count actual intersection potential
        var alternativeScored = allWords
            .Where(w => w.Length >= 5 && w.Length <= Math.Min(10, options.Width - 2))
            .Select(w => 
            {
                var intersectionCount = 0;
                foreach (var letter in w.Text.Distinct())
                {
                    intersectionCount += allWords.Count(other => other != w && other.Text.Contains(letter));
                }
                return new
                {
                    Word = w,
                    IntersectionPotential = intersectionCount,
                    UniqueLetters = w.Text.Distinct().Count(),
                    CurrentScore = w.Text.Count(c => "AEIOU".Contains(c)) * 3 +
                                  w.Text.Count(c => "RNSTL".Contains(c)) * 2 +
                                  w.Text.Count(c => "ÅÄÖ".Contains(c)) * 1 +
                                  ((w.Length >= 5 && w.Length <= 8) ? 5 : 0) +
                                  w.Text.Distinct().Count() * 0.5
                };
            })
            .OrderByDescending(w => w.IntersectionPotential)
            .Take(20)
            .ToList();
        
        Console.WriteLine("Top 20 by INTERSECTION POTENTIAL vs CURRENT SCORE:");
        Console.WriteLine("Word\t\tIntersect\tUnique\tCurrent");
        Console.WriteLine(new string('-', 60));
        
        foreach (var w in alternativeScored)
        {
            var wordPadded = w.Word.Text.PadRight(12);
            Console.WriteLine($"{wordPadded}\t{w.IntersectionPotential}\t\t{w.UniqueLetters}\t{w.CurrentScore:F1}");
        }
        
        // Compare rankings
        Console.WriteLine();
        Console.WriteLine("=== COMPARISON ===");
        
        var currentTopWord = allWords
            .Where(w => w.Length >= 5 && w.Length <= 8)
            .OrderByDescending(w => w.Text.Count(c => "AEIOU".Contains(c)) * 3 +
                                   w.Text.Count(c => "RNSTL".Contains(c)) * 2 +
                                   w.Text.Count(c => "ÅÄÖ".Contains(c)) * 1 +
                                   5 + w.Text.Distinct().Count() * 0.5)
            .First();
        
        var intersectionTopWord = alternativeScored.First().Word;
        
        Console.WriteLine($"Current strategy picks: {currentTopWord.Text}");
        Console.WriteLine($"Intersection-based picks: {intersectionTopWord.Text}");
        
        if (currentTopWord.Text != intersectionTopWord.Text)
        {
            Console.WriteLine("??  BIAS DETECTED: Current strategy may not pick optimal anchor!");
        }
        else
        {
            Console.WriteLine("? Strategies agree on top anchor");
        }
        
        await Assert.That(alternativeScored.Count).IsGreaterThan(0);
    }
    
    [Test]
    public async Task Quick_Bias_Analysis()
    {
        var dictionary = new SwedishDictionary();
        
        if (dictionary.WordCount == 0)
        {
            Console.WriteLine("SKIPPED: No words in dictionary.");
            return;
        }
        
        var options = CrosswordGenerationOptions.Easy;
        var allWords = dictionary.GetWords(minLength: 1, maxLength: options.Width).ToList();
        
        Console.WriteLine($"Dictionary: {dictionary.WordCount} words");
        Console.WriteLine($"Anchor candidates (len 5-{Math.Min(10, options.Width - 2)}): {allWords.Count(w => w.Length >= 5 && w.Length <= Math.Min(10, options.Width - 2))}");
        Console.WriteLine();
        
        // Current strategy scoring
        var currentScored = allWords
            .Where(w => w.Length >= 5 && w.Length <= Math.Min(10, options.Width - 2))
            .Select(w => new {
                Word = w.Text,
                Length = w.Length,
                CurrentScore = w.Text.Count(c => "AEIOU".Contains(c)) * 3 +
                              w.Text.Count(c => "RNSTL".Contains(c)) * 2 +
                              w.Text.Count(c => "ÅÄÖ".Contains(c)) * 1 +
                              ((w.Length >= 5 && w.Length <= 8) ? 5 : 0) +
                              w.Text.Distinct().Count() * 0.5,
                IntersectionPotential = w.Text.Distinct().Sum(letter => 
                    allWords.Count(other => other.Text != w.Text && other.Text.Contains(letter)))
            })
            .OrderByDescending(w => w.CurrentScore)
            .Take(10)
            .ToList();
        
        // Alternative: by intersection potential
        var intersectionScored = allWords
            .Where(w => w.Length >= 5 && w.Length <= Math.Min(10, options.Width - 2))
            .Select(w => new {
                Word = w.Text,
                Length = w.Length,
                CurrentScore = w.Text.Count(c => "AEIOU".Contains(c)) * 3 +
                              w.Text.Count(c => "RNSTL".Contains(c)) * 2 +
                              w.Text.Count(c => "ÅÄÖ".Contains(c)) * 1 +
                              ((w.Length >= 5 && w.Length <= 8) ? 5 : 0) +
                              w.Text.Distinct().Count() * 0.5,
                IntersectionPotential = w.Text.Distinct().Sum(letter => 
                    allWords.Count(other => other.Text != w.Text && other.Text.Contains(letter)))
            })
            .OrderByDescending(w => w.IntersectionPotential)
            .Take(10)
            .ToList();
        
        Console.WriteLine("CURRENT STRATEGY TOP 10:");
        foreach (var w in currentScored)
        {
            Console.WriteLine($"  {w.Word,-14} len={w.Length} score={w.CurrentScore:F1} intersect={w.IntersectionPotential}");
        }
        
        Console.WriteLine();
        Console.WriteLine("INTERSECTION-BASED TOP 10:");
        foreach (var w in intersectionScored)
        {
            Console.WriteLine($"  {w.Word,-14} len={w.Length} score={w.CurrentScore:F1} intersect={w.IntersectionPotential}");
        }
        
        var currentTop = currentScored.First();
        var intersectionTop = intersectionScored.First();
        
        Console.WriteLine();
        Console.WriteLine("=== BIAS ASSESSMENT ===");
        Console.WriteLine($"Current picks: {currentTop.Word} (intersect: {currentTop.IntersectionPotential})");
        Console.WriteLine($"Optimal picks: {intersectionTop.Word} (intersect: {intersectionTop.IntersectionPotential})");
        
        var overlapCount = currentScored.Select(w => w.Word).Intersect(intersectionScored.Select(w => w.Word)).Count();
        Console.WriteLine($"Overlap in top 10: {overlapCount}/10");
        
        if (currentTop.Word != intersectionTop.Word)
        {
            var intersectDiff = intersectionTop.IntersectionPotential - currentTop.IntersectionPotential;
            var percentDiff = (double)intersectDiff / currentTop.IntersectionPotential * 100;
            Console.WriteLine($"BIAS: Current anchor loses {intersectDiff} intersections ({percentDiff:F1}% less)");
        }
        else
        {
            Console.WriteLine("NO BIAS: Strategies agree");
        }
        
        await Assert.That(true).IsTrue();
    }
}
