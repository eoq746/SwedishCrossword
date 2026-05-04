using TUnit.Assertions;
using TUnit.Core;
using SwedishCrossword.Models;

namespace SwedishCrossword.Tests;

/// <summary>
/// Tests for vinkelord (bent word) support in the Word and WordSegment models.
/// </summary>
public class VinkelordTests
{
    #region WordSegment Tests

    [Test]
    public async Task WordSegment_GetPositions_AcrossSegment()
    {
        var segment = new WordSegment
        {
            StartRow = 2,
            StartCol = 5,
            Direction = Direction.Across,
            Length = 4
        };

        var positions = segment.GetPositions().ToList();

        await Assert.That(positions.Count).IsEqualTo(4);
        await Assert.That(positions[0]).IsEqualTo((2, 5));
        await Assert.That(positions[1]).IsEqualTo((2, 6));
        await Assert.That(positions[2]).IsEqualTo((2, 7));
        await Assert.That(positions[3]).IsEqualTo((2, 8));
    }

    [Test]
    public async Task WordSegment_GetPositions_DownSegment()
    {
        var segment = new WordSegment
        {
            StartRow = 1,
            StartCol = 3,
            Direction = Direction.Down,
            Length = 3
        };

        var positions = segment.GetPositions().ToList();

        await Assert.That(positions.Count).IsEqualTo(3);
        await Assert.That(positions[0]).IsEqualTo((1, 3));
        await Assert.That(positions[1]).IsEqualTo((2, 3));
        await Assert.That(positions[2]).IsEqualTo((3, 3));
    }

    [Test]
    public async Task WordSegment_EndRow_Across()
    {
        var segment = new WordSegment
        {
            StartRow = 5,
            StartCol = 2,
            Direction = Direction.Across,
            Length = 4
        };

        await Assert.That(segment.EndRow).IsEqualTo(5);
        await Assert.That(segment.EndCol).IsEqualTo(5);
    }

    [Test]
    public async Task WordSegment_EndRow_Down()
    {
        var segment = new WordSegment
        {
            StartRow = 1,
            StartCol = 7,
            Direction = Direction.Down,
            Length = 5
        };

        await Assert.That(segment.EndRow).IsEqualTo(5);
        await Assert.That(segment.EndCol).IsEqualTo(7);
    }

    #endregion

    #region Word with Segments - L-shape (1 bend)

    [Test]
    public async Task Word_OneBend_AcrossThenDown_GetPositions()
    {
        // Word "KORSORD" (7 letters) as L-shape:
        // Across: (2,5)K (2,6)O (2,7)R (2,8)S  [4 cells]
        // Down:   (2,8)S (3,8)O (4,8)R (5,8)D  [4 cells]
        // Shared cell: (2,8) = 'S'
        // Total unique positions: 4 + 4 - 1 = 7

        var word = new Word("KORSORD", "Puzzle")
        {
            StartRow = 2,
            StartColumn = 5,
            Direction = Direction.Across,
            IsPlaced = true,
            Segments =
            [
                new WordSegment { StartRow = 2, StartCol = 5, Direction = Direction.Across, Length = 4 },
                new WordSegment { StartRow = 2, StartCol = 8, Direction = Direction.Down, Length = 4 }
            ]
        };

        var positions = word.GetPositions().ToList();

        await Assert.That(positions.Count).IsEqualTo(7);
        // First segment: K O R S
        await Assert.That(positions[0]).IsEqualTo((2, 5));
        await Assert.That(positions[1]).IsEqualTo((2, 6));
        await Assert.That(positions[2]).IsEqualTo((2, 7));
        await Assert.That(positions[3]).IsEqualTo((2, 8)); // Bend cell (S)
        // Second segment: O R D (skipping the shared S)
        await Assert.That(positions[4]).IsEqualTo((3, 8));
        await Assert.That(positions[5]).IsEqualTo((4, 8));
        await Assert.That(positions[6]).IsEqualTo((5, 8));
    }

    [Test]
    public async Task Word_OneBend_DownThenAcross_GetPositions()
    {
        // Word "ABCDE" (5 letters) as L-shape:
        // Down:   (1,3)A (2,3)B (3,3)C  [3 cells]
        // Across: (3,3)C (3,4)D (3,5)E  [3 cells]
        // Shared cell: (3,3) = 'C'
        // Total unique: 3 + 3 - 1 = 5

        var word = new Word("ABCDE", "Test")
        {
            StartRow = 1,
            StartColumn = 3,
            Direction = Direction.Down,
            IsPlaced = true,
            Segments =
            [
                new WordSegment { StartRow = 1, StartCol = 3, Direction = Direction.Down, Length = 3 },
                new WordSegment { StartRow = 3, StartCol = 3, Direction = Direction.Across, Length = 3 }
            ]
        };

        var positions = word.GetPositions().ToList();

        await Assert.That(positions.Count).IsEqualTo(5);
        await Assert.That(positions[0]).IsEqualTo((1, 3)); // A
        await Assert.That(positions[1]).IsEqualTo((2, 3)); // B
        await Assert.That(positions[2]).IsEqualTo((3, 3)); // C (bend)
        await Assert.That(positions[3]).IsEqualTo((3, 4)); // D
        await Assert.That(positions[4]).IsEqualTo((3, 5)); // E
    }

    [Test]
    public async Task Word_OneBend_EndRowEndColumn()
    {
        var word = new Word("KORSORD", "Puzzle")
        {
            StartRow = 2,
            StartColumn = 5,
            Direction = Direction.Across,
            IsPlaced = true,
            Segments =
            [
                new WordSegment { StartRow = 2, StartCol = 5, Direction = Direction.Across, Length = 4 },
                new WordSegment { StartRow = 2, StartCol = 8, Direction = Direction.Down, Length = 4 }
            ]
        };

        await Assert.That(word.EndRow).IsEqualTo(5);
        await Assert.That(word.EndColumn).IsEqualTo(8);
    }

    [Test]
    public async Task Word_OneBend_IsBent()
    {
        var word = new Word("ABCDE", "Test")
        {
            Segments =
            [
                new WordSegment { StartRow = 0, StartCol = 0, Direction = Direction.Across, Length = 3 },
                new WordSegment { StartRow = 0, StartCol = 2, Direction = Direction.Down, Length = 3 }
            ]
        };

        await Assert.That(word.IsBent).IsTrue();
        await Assert.That(word.BendCount).IsEqualTo(1);
    }

    #endregion

    #region Word with Segments - Z/S-shape (2 bends)

    [Test]
    public async Task Word_TwoBends_ZShape_GetPositions()
    {
        // Word "STOCKHOLM" (9 letters) as Z-shape:
        // Across: (1,3)S (1,4)T (1,5)O (1,6)C  [4 cells]
        // Down:   (1,6)C (2,6)K (3,6)H          [3 cells]
        // Across: (3,6)H (3,7)O (3,8)L (3,9)M  [4 cells]
        // Total: 4 + 3 + 4 - 2 = 9

        var word = new Word("STOCKHOLM", "Capital")
        {
            StartRow = 1,
            StartColumn = 3,
            Direction = Direction.Across,
            IsPlaced = true,
            Segments =
            [
                new WordSegment { StartRow = 1, StartCol = 3, Direction = Direction.Across, Length = 4 },
                new WordSegment { StartRow = 1, StartCol = 6, Direction = Direction.Down, Length = 3 },
                new WordSegment { StartRow = 3, StartCol = 6, Direction = Direction.Across, Length = 4 }
            ]
        };

        var positions = word.GetPositions().ToList();

        await Assert.That(positions.Count).IsEqualTo(9);
        // First segment: S T O C
        await Assert.That(positions[0]).IsEqualTo((1, 3));
        await Assert.That(positions[1]).IsEqualTo((1, 4));
        await Assert.That(positions[2]).IsEqualTo((1, 5));
        await Assert.That(positions[3]).IsEqualTo((1, 6)); // C (first bend)
        // Second segment: K H (skip shared C)
        await Assert.That(positions[4]).IsEqualTo((2, 6));
        await Assert.That(positions[5]).IsEqualTo((3, 6)); // H (second bend)
        // Third segment: O L M (skip shared H)
        await Assert.That(positions[6]).IsEqualTo((3, 7));
        await Assert.That(positions[7]).IsEqualTo((3, 8));
        await Assert.That(positions[8]).IsEqualTo((3, 9));
    }

    [Test]
    public async Task Word_TwoBends_BendCount()
    {
        var word = new Word("STOCKHOLM", "Capital")
        {
            Segments =
            [
                new WordSegment { StartRow = 1, StartCol = 3, Direction = Direction.Across, Length = 4 },
                new WordSegment { StartRow = 1, StartCol = 6, Direction = Direction.Down, Length = 3 },
                new WordSegment { StartRow = 3, StartCol = 6, Direction = Direction.Across, Length = 4 }
            ]
        };

        await Assert.That(word.IsBent).IsTrue();
        await Assert.That(word.BendCount).IsEqualTo(2);
    }

    [Test]
    public async Task Word_TwoBends_EndRowEndColumn()
    {
        var word = new Word("STOCKHOLM", "Capital")
        {
            Segments =
            [
                new WordSegment { StartRow = 1, StartCol = 3, Direction = Direction.Across, Length = 4 },
                new WordSegment { StartRow = 1, StartCol = 6, Direction = Direction.Down, Length = 3 },
                new WordSegment { StartRow = 3, StartCol = 6, Direction = Direction.Across, Length = 4 }
            ]
        };

        await Assert.That(word.EndRow).IsEqualTo(3);
        await Assert.That(word.EndColumn).IsEqualTo(9);
    }

    #endregion

    #region Straight Word Backward Compatibility

    [Test]
    public async Task Word_NoSegments_IsStraight()
    {
        var word = new Word("TEST", "Clue");

        await Assert.That(word.IsBent).IsFalse();
        await Assert.That(word.BendCount).IsEqualTo(0);
        await Assert.That(word.Segments).IsEmpty();
    }

    [Test]
    public async Task Word_NoSegments_GetPositionsUnchanged()
    {
        var word = new Word("CAT", "Animal")
        {
            StartRow = 2,
            StartColumn = 3,
            Direction = Direction.Across,
            IsPlaced = true
        };

        var positions = word.GetPositions().ToList();

        await Assert.That(positions.Count).IsEqualTo(3);
        await Assert.That(positions[0]).IsEqualTo((2, 3));
        await Assert.That(positions[1]).IsEqualTo((2, 4));
        await Assert.That(positions[2]).IsEqualTo((2, 5));
    }

    [Test]
    public async Task Word_NoSegments_EndRowEndColumnUnchanged()
    {
        var word = new Word("HELLO", "Greeting")
        {
            StartRow = 3,
            StartColumn = 2,
            Direction = Direction.Across,
            IsPlaced = true
        };

        await Assert.That(word.EndRow).IsEqualTo(3);
        await Assert.That(word.EndColumn).IsEqualTo(6);
    }

    #endregion

    #region GetDirectionAtIndex Tests

    [Test]
    public async Task Word_GetDirectionAtIndex_StraightWord()
    {
        var word = new Word("TEST", "Clue")
        {
            Direction = Direction.Across
        };

        await Assert.That(word.GetDirectionAtIndex(0)).IsEqualTo(Direction.Across);
        await Assert.That(word.GetDirectionAtIndex(3)).IsEqualTo(Direction.Across);
    }

    [Test]
    public async Task Word_GetDirectionAtIndex_BentWord()
    {
        // "ABCDE" (5 letters): Across 3 + Down 3, shared at index 2
        var word = new Word("ABCDE", "Test")
        {
            Direction = Direction.Across,
            Segments =
            [
                new WordSegment { StartRow = 0, StartCol = 0, Direction = Direction.Across, Length = 3 },
                new WordSegment { StartRow = 0, StartCol = 2, Direction = Direction.Down, Length = 3 }
            ]
        };

        // First segment covers indices 0,1,2 (length 3)
        await Assert.That(word.GetDirectionAtIndex(0)).IsEqualTo(Direction.Across);
        await Assert.That(word.GetDirectionAtIndex(1)).IsEqualTo(Direction.Across);
        await Assert.That(word.GetDirectionAtIndex(2)).IsEqualTo(Direction.Across); // Bend cell belongs to first segment
        // Second segment covers indices 3,4 (length 3 minus 1 shared = 2)
        await Assert.That(word.GetDirectionAtIndex(3)).IsEqualTo(Direction.Down);
        await Assert.That(word.GetDirectionAtIndex(4)).IsEqualTo(Direction.Down);
    }

    #endregion

    #region Intersection Tests with Bent Words

    [Test]
    public async Task Word_BentWord_IntersectsWithStraightWord()
    {
        // Bent word goes Across then Down
        var bent = new Word("ABCDE", "Bent")
        {
            StartRow = 2,
            StartColumn = 3,
            Direction = Direction.Across,
            IsPlaced = true,
            Segments =
            [
                new WordSegment { StartRow = 2, StartCol = 3, Direction = Direction.Across, Length = 3 },
                new WordSegment { StartRow = 2, StartCol = 5, Direction = Direction.Down, Length = 3 }
            ]
        };
        // Positions: (2,3) (2,4) (2,5) (3,5) (4,5)

        // Straight word goes Across at row 3
        var straight = new Word("XYZ", "Straight")
        {
            StartRow = 3,
            StartColumn = 4,
            Direction = Direction.Across,
            IsPlaced = true
        };
        // Positions: (3,4) (3,5) (3,6)

        // They share (3,5)
        await Assert.That(bent.IntersectsWith(straight)).IsTrue();
    }

    [Test]
    public async Task Word_BentWord_GetIntersectionsWithStraightWord()
    {
        var bent = new Word("ABCDE", "Bent")
        {
            StartRow = 2,
            StartColumn = 3,
            Direction = Direction.Across,
            IsPlaced = true,
            Segments =
            [
                new WordSegment { StartRow = 2, StartCol = 3, Direction = Direction.Across, Length = 3 },
                new WordSegment { StartRow = 2, StartCol = 5, Direction = Direction.Down, Length = 3 }
            ]
        };

        var straight = new Word("XDZ", "Straight")
        {
            StartRow = 3,
            StartColumn = 4,
            Direction = Direction.Across,
            IsPlaced = true
        };

        var intersections = bent.GetIntersections(straight).ToList();

        await Assert.That(intersections.Count).IsEqualTo(1);
        await Assert.That(intersections[0].Row).IsEqualTo(3);
        await Assert.That(intersections[0].Column).IsEqualTo(5);
        await Assert.That(intersections[0].MyIndex).IsEqualTo(3); // 'D' in bent word
        await Assert.That(intersections[0].OtherIndex).IsEqualTo(1); // 'D' in straight word
    }

    #endregion

    #region GridCell BendArrowDirection Tests

    [Test]
    public async Task GridCell_BendArrowDirection_DefaultsToNull()
    {
        var cell = new GridCell();

        await Assert.That(cell.BendArrowDirection).IsNull();
    }

    [Test]
    public async Task GridCell_Clear_ResetsBendArrowDirection()
    {
        var cell = new GridCell
        {
            BendArrowDirection = Direction.Down
        };

        cell.Clear();

        await Assert.That(cell.BendArrowDirection).IsNull();
    }

    #endregion

    #region CrosswordGenerationOptions Tests

    [Test]
    public async Task Options_MaxVinkelordLength_ComputedFromDimensions()
    {
        var options = new SwedishCrossword.Services.CrosswordGenerationOptions
        {
            Width = 15,
            Height = 15
        };

        await Assert.That(options.MaxVinkelordLength).IsEqualTo(29);
    }

    [Test]
    public async Task Options_MaxVinkelordLength_AsymmetricGrid()
    {
        var options = new SwedishCrossword.Services.CrosswordGenerationOptions
        {
            Width = 11,
            Height = 9
        };

        await Assert.That(options.MaxVinkelordLength).IsEqualTo(19);
    }

    #endregion

    #region CrosswordGrid Bent Word Placement Tests

    [Test]
    public async Task Grid_TryPlaceBentWord_LShape_Success()
    {
        var grid = new CrosswordGrid(10, 10);

        // Place "ABCDE" as L-shape: Across (2,3)?(2,5), then Down (2,5)?(4,5)
        var word = new Word("ABCDE", "Test");
        var segments = new List<WordSegment>
        {
            new() { StartRow = 2, StartCol = 3, Direction = Direction.Across, Length = 3 },
            new() { StartRow = 2, StartCol = 5, Direction = Direction.Down, Length = 3 }
        };
        grid.GetCell(1, 5).Block();

        var placed = grid.TryPlaceBentWordWithValidation(word, segments);

        await Assert.That(placed).IsTrue();
        await Assert.That(word.IsPlaced).IsTrue();
        await Assert.That(word.IsBent).IsTrue();
        await Assert.That(word.BendCount).IsEqualTo(1);
        await Assert.That(grid.GetCell(2, 3).Letter).IsEqualTo('A');
        await Assert.That(grid.GetCell(2, 4).Letter).IsEqualTo('B');
        await Assert.That(grid.GetCell(2, 5).Letter).IsEqualTo('C'); // Bend cell
        await Assert.That(grid.GetCell(3, 5).Letter).IsEqualTo('D');
        await Assert.That(grid.GetCell(4, 5).Letter).IsEqualTo('E');
    }

    [Test]
    public async Task Grid_TryPlaceBentWord_SetsArrowDirection()
    {
        var grid = new CrosswordGrid(10, 10);

        var word = new Word("ABCDE", "Test");
        var segments = new List<WordSegment>
        {
            new() { StartRow = 2, StartCol = 3, Direction = Direction.Across, Length = 3 },
            new() { StartRow = 2, StartCol = 5, Direction = Direction.Down, Length = 3 }
        };
        grid.GetCell(1, 5).Block();

        var placed = grid.TryPlaceBentWordWithValidation(word, segments);
        await Assert.That(placed).IsTrue();

        // Bend cell should have arrow pointing Down
        await Assert.That(grid.GetCell(2, 5).BendArrowDirection).IsEqualTo(Direction.Down);
        // Non-bend cells should not have arrow
        await Assert.That(grid.GetCell(2, 3).BendArrowDirection).IsNull();
        await Assert.That(grid.GetCell(4, 5).BendArrowDirection).IsNull();
    }

    [Test]
    public async Task Grid_TryPlaceBentWord_RejectsOutOfBounds()
    {
        var grid = new CrosswordGrid(5, 5);

        // "ABCDEFGHI" (9 letters): Across 4 + Down 6 - 1 = 9
        // Down segment from (0,3) length 6 would reach row 5 which is out of bounds for height 5
        var word = new Word("ABCDEFGHI", "Too long");
        var segments = new List<WordSegment>
        {
            new() { StartRow = 0, StartCol = 0, Direction = Direction.Across, Length = 4 },
            new() { StartRow = 0, StartCol = 3, Direction = Direction.Down, Length = 6 }
        };

        var placed = grid.TryPlaceBentWordWithValidation(word, segments);

        await Assert.That(placed).IsFalse();
    }

    [Test]
    public async Task Grid_TryPlaceBentWord_RejectsLetterConflict()
    {
        var grid = new CrosswordGrid(10, 10);

        // Place a straight word first
        var straight = new Word("XYZ", "Straight");
        grid.TryPlaceWord(straight, 3, 5, Direction.Across);

        // Try to place a bent word that conflicts at (3,5)
        var bent = new Word("ABCDE", "Bent");
        var segments = new List<WordSegment>
        {
            new() { StartRow = 2, StartCol = 5, Direction = Direction.Down, Length = 3 },
            new() { StartRow = 4, StartCol = 5, Direction = Direction.Across, Length = 3 }
        };
        // Position (3,5) would have 'B' from bent but 'X' from straight

        var placed = grid.TryPlaceBentWordWithValidation(bent, segments);

        await Assert.That(placed).IsFalse();
    }

    [Test]
    public async Task Grid_TryPlaceBentWord_AllowsMatchingIntersection()
    {
        var grid = new CrosswordGrid(10, 10);

        // Place "SOL" across at row 3
        var straight = new Word("SOL", "Lyser på dagen");
        grid.TryPlaceWord(straight, 3, 3, Direction.Across);
        // Positions: (3,3)S (3,4)O (3,5)L

        // Place bent word "STOL" that intersects at 'O' and 'L' positions
        // Down (1,4)->(3,4), then Across (3,4)->(3,5)
        var bent = new Word("STOL", "Möbel att sitta på");
        var segments = new List<WordSegment>
        {
            new() { StartRow = 1, StartCol = 4, Direction = Direction.Down,   Length = 3 },
            new() { StartRow = 3, StartCol = 4, Direction = Direction.Across, Length = 2 }
        };
        grid.GetCell(4, 4).Block();
        // (1,4)S (2,4)T (3,4)O [bend] (3,5)L
        // At (3,4): bent has 'O', straight has 'O' -- match!
        // At (3,5): bent has 'L', straight has 'L' -- match!

        var placed = grid.TryPlaceBentWordWithValidation(bent, segments);

        await Assert.That(placed).IsTrue();
    }

    [Test]
    public async Task Grid_TryPlaceBentWord_ZShape_TwoBends()
    {
        var grid = new CrosswordGrid(15, 15);

        // "ABCDEFGHI" (9 letters) as Z-shape:
        // Across (1,3)?(1,6): A B C D [4 cells]
        // Down (1,6)?(3,6): D E F [3 cells]
        // Across (3,6)?(3,9): F G H I [4 cells]
        // Total: 4 + 3 + 4 - 2 = 9
        var word = new Word("ABCDEFGHI", "Test Z");
        var segments = new List<WordSegment>
        {
            new() { StartRow = 1, StartCol = 3, Direction = Direction.Across, Length = 4 },
            new() { StartRow = 1, StartCol = 6, Direction = Direction.Down, Length = 3 },
            new() { StartRow = 3, StartCol = 6, Direction = Direction.Across, Length = 4 }
        };
        grid.GetCell(0, 6).Block();
        grid.GetCell(4, 6).Block();

        var placed = grid.TryPlaceBentWordWithValidation(word, segments);

        await Assert.That(placed).IsTrue();
        await Assert.That(word.BendCount).IsEqualTo(2);
        await Assert.That(grid.GetCell(1, 3).Letter).IsEqualTo('A');
        await Assert.That(grid.GetCell(1, 6).Letter).IsEqualTo('D'); // First bend
        await Assert.That(grid.GetCell(2, 6).Letter).IsEqualTo('E');
        await Assert.That(grid.GetCell(3, 6).Letter).IsEqualTo('F'); // Second bend
        await Assert.That(grid.GetCell(3, 9).Letter).IsEqualTo('I');

        // Check arrow directions at bend cells
        await Assert.That(grid.GetCell(1, 6).BendArrowDirection).IsEqualTo(Direction.Down);
        await Assert.That(grid.GetCell(3, 6).BendArrowDirection).IsEqualTo(Direction.Across);
    }

    #endregion
}
