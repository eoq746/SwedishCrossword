using TUnit.Assertions;
using TUnit.Core;
using SwedishCrossword.Models;

namespace SwedishCrossword.Tests;

/// <summary>
/// Unit tests for the GridCell class
/// </summary>
public class GridCellTests
{
    [Test]
    public async Task NewCell_HasCorrectDefaultState()
    {
        var cell = new GridCell();

        await Assert.That(cell.IsEmpty).IsTrue();
        await Assert.That(cell.HasLetter).IsFalse();
        await Assert.That(cell.IsBlocked).IsFalse();
        await Assert.That(cell.IsPartOfWord).IsFalse();
        await Assert.That(cell.IsNumbered).IsFalse();
        await Assert.That(cell.Letter).IsEqualTo('\0');
        await Assert.That(cell.Number).IsEqualTo(0);
        await Assert.That(cell.WordIds).IsEmpty();
    }

    [Test]
    public async Task SetLetter_UpdatesCellProperties()
    {
        var cell = new GridCell();
        const string wordId = "word-123";

        cell.SetLetter('k', wordId);

        await Assert.That(cell.Letter).IsEqualTo('K');
        await Assert.That(cell.HasLetter).IsTrue();
        await Assert.That(cell.IsPartOfWord).IsTrue();
        await Assert.That(cell.WordIds).Contains(wordId);
        await Assert.That(cell.IsEmpty).IsFalse();
    }

    [Test]
    public async Task SetLetter_ConvertsToUppercase()
    {
        var cell = new GridCell();

        cell.SetLetter('a', "test");

        await Assert.That(cell.Letter).IsEqualTo('A');
    }

    [Test]
    [Arguments('å', 'Å')]
    [Arguments('ä', 'Ä')]
    [Arguments('ö', 'Ö')]
    public async Task SetLetter_HandlesSwedishCharacters(char input, char expected)
    {
        var cell = new GridCell();

        cell.SetLetter(input, "test");

        await Assert.That(cell.Letter).IsEqualTo(expected);
    }

    [Test]
    public async Task SetLetter_AllowsMultipleWordIds()
    {
        var cell = new GridCell();

        cell.SetLetter('A', "horizontal-word");
        cell.SetLetter('A', "vertical-word");

        await Assert.That(cell.WordIds.Count).IsEqualTo(2);
        await Assert.That(cell.WordIds).Contains("horizontal-word");
        await Assert.That(cell.WordIds).Contains("vertical-word");
    }

    [Test]
    public async Task Block_ClearsAllCellContent()
    {
        var cell = new GridCell();
        cell.SetLetter('A', "word1");
        cell.Number = 5;

        cell.Block();

        await Assert.That(cell.IsBlocked).IsTrue();
        await Assert.That(cell.HasLetter).IsFalse();
        await Assert.That(cell.IsPartOfWord).IsFalse();
        await Assert.That(cell.Number).IsEqualTo(0);
        await Assert.That(cell.WordIds).IsEmpty();
        await Assert.That(cell.Letter).IsEqualTo('\0');
    }

    [Test]
    public async Task Clear_ResetsCell()
    {
        var cell = new GridCell();
        cell.SetLetter('A', "word1");
        cell.Number = 3;

        cell.Clear();

        await Assert.That(cell.IsEmpty).IsTrue();
        await Assert.That(cell.HasLetter).IsFalse();
        await Assert.That(cell.IsBlocked).IsFalse();
        await Assert.That(cell.Number).IsEqualTo(0);
        await Assert.That(cell.WordIds).IsEmpty();
    }

    [Test]
    public async Task IsNumbered_ReturnsTrueWhenNumberIsSet()
    {
        var cell = new GridCell();

        await Assert.That(cell.IsNumbered).IsFalse();

        cell.Number = 1;

        await Assert.That(cell.IsNumbered).IsTrue();
    }

    [Test]
    public async Task ToString_ReturnsSpaceForEmptyCell()
    {
        var cell = new GridCell();

        await Assert.That(cell.ToString()).IsEqualTo(" ");
    }

    [Test]
    public async Task ToString_ReturnsLetterForFilledCell()
    {
        var cell = new GridCell();
        cell.SetLetter('K', "word");

        await Assert.That(cell.ToString()).IsEqualTo("K");
    }

    [Test]
    public async Task ToString_ReturnsHashForBlockedCell()
    {
        var cell = new GridCell();
        cell.Block();

        await Assert.That(cell.ToString()).IsEqualTo("#");
    }

    [Test]
    public async Task ToString_ReturnsAsteriskForAsteriskCell()
    {
        var cell = new GridCell();
        cell.Letter = '*';

        await Assert.That(cell.ToString()).IsEqualTo("*");
        await Assert.That(cell.HasAsterisk).IsTrue();
        await Assert.That(cell.HasLetter).IsFalse();
    }
}
