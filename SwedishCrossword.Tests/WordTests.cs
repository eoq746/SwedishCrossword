using TUnit.Assertions;
using TUnit.Core;
using SwedishCrossword.Models;

namespace SwedishCrossword.Tests;

/// <summary>
/// Unit tests for the Word model class
/// </summary>
public class WordTests
{
    [Test]
    public async Task Constructor_NormalizesTextToUppercase()
    {
        var word = new Word("katt", "En husdjur", "Djur", DifficultyLevel.Easy);

        await Assert.That(word.Text).IsEqualTo("KATT");
    }

    [Test]
    public async Task Constructor_TrimsWhitespace()
    {
        var word = new Word("  test  ", "  clue  ");

        await Assert.That(word.Text).IsEqualTo("TEST");
        await Assert.That(word.Clue).IsEqualTo("clue");
    }

    [Test]
    public async Task Constructor_SetsAllProperties()
    {
        var word = new Word("KATT", "En husdjur", "Djur", DifficultyLevel.Easy);

        await Assert.That(word.Text).IsEqualTo("KATT");
        await Assert.That(word.Clue).IsEqualTo("En husdjur");
        await Assert.That(word.Category).IsEqualTo("Djur");
        await Assert.That(word.Difficulty).IsEqualTo(DifficultyLevel.Easy);
        await Assert.That(word.Length).IsEqualTo(4);
    }

    [Test]
    public async Task Constructor_SetsDefaultValues()
    {
        var word = new Word("TEST", "Clue");

        await Assert.That(word.Category).IsEqualTo("");
        await Assert.That(word.Difficulty).IsEqualTo(DifficultyLevel.Medium);
    }

    [Test]
    public async Task NewWord_IsNotPlaced()
    {
        var word = new Word("TEST", "Test clue");

        await Assert.That(word.IsPlaced).IsFalse();
        await Assert.That(word.Number).IsEqualTo(0);
        await Assert.That(word.StartRow).IsEqualTo(-1);
        await Assert.That(word.StartColumn).IsEqualTo(-1);
    }

    [Test]
    public async Task NewWord_HasUniqueId()
    {
        var word1 = new Word("TEST", "Clue");
        var word2 = new Word("TEST", "Clue");

        await Assert.That(word1.Id).IsNotEqualTo(word2.Id);
        await Assert.That(word1.Id).IsNotEmpty();
    }

    [Test]
    public async Task Length_ReturnsTextLength()
    {
        var word = new Word("HELLO", "Greeting");

        await Assert.That(word.Length).IsEqualTo(5);
    }

    [Test]
    public async Task GetCharAt_ReturnsCorrectCharacter()
    {
        var word = new Word("KATT", "Test");

        await Assert.That(word.GetCharAt(0)).IsEqualTo('K');
        await Assert.That(word.GetCharAt(1)).IsEqualTo('A');
        await Assert.That(word.GetCharAt(2)).IsEqualTo('T');
        await Assert.That(word.GetCharAt(3)).IsEqualTo('T');
    }

    [Test]
    public async Task GetCharAt_ThrowsForNegativePosition()
    {
        var word = new Word("TEST", "Clue");

        await Assert.That(() => word.GetCharAt(-1))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task GetCharAt_ThrowsForPositionBeyondLength()
    {
        var word = new Word("TEST", "Clue");

        await Assert.That(() => word.GetCharAt(4))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task GetPositions_ReturnsEmptyForUnplacedWord()
    {
        var word = new Word("TEST", "Clue");

        var positions = word.GetPositions().ToList();

        await Assert.That(positions).IsEmpty();
    }

    [Test]
    public async Task GetPositions_ReturnsCorrectPositionsForAcrossWord()
    {
        var word = new Word("CAT", "Animal");
        word.StartRow = 2;
        word.StartColumn = 3;
        word.Direction = Direction.Across;
        word.IsPlaced = true;

        var positions = word.GetPositions().ToList();

        await Assert.That(positions.Count).IsEqualTo(3);
        await Assert.That(positions[0]).IsEqualTo((2, 3));
        await Assert.That(positions[1]).IsEqualTo((2, 4));
        await Assert.That(positions[2]).IsEqualTo((2, 5));
    }

    [Test]
    public async Task GetPositions_ReturnsCorrectPositionsForDownWord()
    {
        var word = new Word("DOG", "Pet");
        word.StartRow = 1;
        word.StartColumn = 5;
        word.Direction = Direction.Down;
        word.IsPlaced = true;

        var positions = word.GetPositions().ToList();

        await Assert.That(positions.Count).IsEqualTo(3);
        await Assert.That(positions[0]).IsEqualTo((1, 5));
        await Assert.That(positions[1]).IsEqualTo((2, 5));
        await Assert.That(positions[2]).IsEqualTo((3, 5));
    }

    [Test]
    public async Task EndRow_CalculatedCorrectlyForAcross()
    {
        var word = new Word("HELLO", "Greeting");
        word.StartRow = 3;
        word.StartColumn = 2;
        word.Direction = Direction.Across;
        word.IsPlaced = true;

        await Assert.That(word.EndRow).IsEqualTo(3);
    }

    [Test]
    public async Task EndColumn_CalculatedCorrectlyForAcross()
    {
        var word = new Word("HELLO", "Greeting");
        word.StartRow = 3;
        word.StartColumn = 2;
        word.Direction = Direction.Across;
        word.IsPlaced = true;

        await Assert.That(word.EndColumn).IsEqualTo(6);
    }

    [Test]
    public async Task EndRow_CalculatedCorrectlyForDown()
    {
        var word = new Word("HELLO", "Greeting");
        word.StartRow = 3;
        word.StartColumn = 2;
        word.Direction = Direction.Down;
        word.IsPlaced = true;

        await Assert.That(word.EndRow).IsEqualTo(7);
    }

    [Test]
    public async Task EndColumn_CalculatedCorrectlyForDown()
    {
        var word = new Word("HELLO", "Greeting");
        word.StartRow = 3;
        word.StartColumn = 2;
        word.Direction = Direction.Down;
        word.IsPlaced = true;

        await Assert.That(word.EndColumn).IsEqualTo(2);
    }

    [Test]
    public async Task IntersectsWith_ReturnsFalseForUnplacedWords()
    {
        var word1 = new Word("TEST", "Clue1");
        var word2 = new Word("TEST", "Clue2");

        await Assert.That(word1.IntersectsWith(word2)).IsFalse();
    }

    [Test]
    public async Task IntersectsWith_ReturnsTrueForIntersectingWords()
    {
        var word1 = new Word("CAT", "Animal");
        word1.StartRow = 2;
        word1.StartColumn = 2;
        word1.Direction = Direction.Across;
        word1.IsPlaced = true;

        var word2 = new Word("ACE", "Card");
        word2.StartRow = 1;
        word2.StartColumn = 3;
        word2.Direction = Direction.Down;
        word2.IsPlaced = true;

        await Assert.That(word1.IntersectsWith(word2)).IsTrue();
    }

    [Test]
    public async Task IntersectsWith_ReturnsFalseForNonIntersectingWords()
    {
        var word1 = new Word("CAT", "Animal");
        word1.StartRow = 2;
        word1.StartColumn = 2;
        word1.Direction = Direction.Across;
        word1.IsPlaced = true;

        var word2 = new Word("DOG", "Pet");
        word2.StartRow = 5;
        word2.StartColumn = 5;
        word2.Direction = Direction.Down;
        word2.IsPlaced = true;

        await Assert.That(word1.IntersectsWith(word2)).IsFalse();
    }

    [Test]
    public async Task GetIntersections_ReturnsEmptyForParallelWords()
    {
        var word1 = new Word("CAT", "Animal");
        word1.StartRow = 2;
        word1.StartColumn = 2;
        word1.Direction = Direction.Across;
        word1.IsPlaced = true;

        var word2 = new Word("DOG", "Pet");
        word2.StartRow = 4;
        word2.StartColumn = 2;
        word2.Direction = Direction.Across;
        word2.IsPlaced = true;

        var intersections = word1.GetIntersections(word2).ToList();

        await Assert.That(intersections).IsEmpty();
    }

    [Test]
    public async Task ToString_ContainsWordInfo()
    {
        var word = new Word("TEST", "A test clue");
        word.Number = 5;
        word.Direction = Direction.Across;

        var result = word.ToString();

        await Assert.That(result).Contains("TEST");
        await Assert.That(result).Contains("5");
        await Assert.That(result).Contains("Across");
        await Assert.That(result).Contains("A test clue");
    }
}
