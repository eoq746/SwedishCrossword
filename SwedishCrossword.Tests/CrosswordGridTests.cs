using TUnit.Assertions;
using TUnit.Core;
using SwedishCrossword.Models;

namespace SwedishCrossword.Tests;

/// <summary>
/// Unit tests for the CrosswordGrid class
/// </summary>
public class CrosswordGridTests
{
    [Test]
    public async Task Constructor_CreatesGridWithCorrectDimensions()
    {
        var grid = new CrosswordGrid(8, 6);

        await Assert.That(grid.Width).IsEqualTo(8);
        await Assert.That(grid.Height).IsEqualTo(6);
        await Assert.That(grid.Words.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Constructor_ThrowsForZeroWidth()
    {
        await Assert.That(() => new CrosswordGrid(0, 5))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Constructor_ThrowsForZeroHeight()
    {
        await Assert.That(() => new CrosswordGrid(5, 0))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Constructor_ThrowsForNegativeWidth()
    {
        await Assert.That(() => new CrosswordGrid(-1, 5))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Constructor_ThrowsForNegativeHeight()
    {
        await Assert.That(() => new CrosswordGrid(5, -1))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task IsValidPosition_ReturnsTrueForValidPositions()
    {
        var grid = new CrosswordGrid(3, 3);

        await Assert.That(grid.IsValidPosition(0, 0)).IsTrue();
        await Assert.That(grid.IsValidPosition(2, 2)).IsTrue();
        await Assert.That(grid.IsValidPosition(1, 1)).IsTrue();
    }

    [Test]
    public async Task IsValidPosition_ReturnsFalseForNegativeRow()
    {
        var grid = new CrosswordGrid(3, 3);

        await Assert.That(grid.IsValidPosition(-1, 0)).IsFalse();
    }

    [Test]
    public async Task IsValidPosition_ReturnsFalseForNegativeColumn()
    {
        var grid = new CrosswordGrid(3, 3);

        await Assert.That(grid.IsValidPosition(0, -1)).IsFalse();
    }

    [Test]
    public async Task IsValidPosition_ReturnsFalseForRowOutOfBounds()
    {
        var grid = new CrosswordGrid(3, 3);

        await Assert.That(grid.IsValidPosition(3, 0)).IsFalse();
    }

    [Test]
    public async Task IsValidPosition_ReturnsFalseForColumnOutOfBounds()
    {
        var grid = new CrosswordGrid(3, 3);

        await Assert.That(grid.IsValidPosition(0, 3)).IsFalse();
    }

    [Test]
    public async Task GetCell_ReturnsEmptyCell()
    {
        var grid = new CrosswordGrid(5, 5);

        var cell = grid.GetCell(2, 3);

        await Assert.That(cell).IsNotNull();
        await Assert.That(cell.IsEmpty).IsTrue();
    }

    [Test]
    public async Task GetCell_ThrowsForRowOutOfBounds()
    {
        var grid = new CrosswordGrid(3, 3);

        await Assert.That(() => grid.GetCell(3, 0))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task GetCell_ThrowsForColumnOutOfBounds()
    {
        var grid = new CrosswordGrid(3, 3);

        await Assert.That(() => grid.GetCell(0, 3))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task TryPlaceWord_PlacesWordCorrectlyAcross()
    {
        var grid = new CrosswordGrid(10, 10);
        var word = new Word("DOG", "Pet");

        var success = grid.TryPlaceWord(word, 3, 2, Direction.Across);

        await Assert.That(success).IsTrue();
        await Assert.That(word.IsPlaced).IsTrue();
        await Assert.That(word.StartRow).IsEqualTo(3);
        await Assert.That(word.StartColumn).IsEqualTo(2);
        await Assert.That(word.Direction).IsEqualTo(Direction.Across);
        await Assert.That(word.Number).IsGreaterThan(0);

        await Assert.That(grid.GetCell(3, 2).Letter).IsEqualTo('D');
        await Assert.That(grid.GetCell(3, 3).Letter).IsEqualTo('O');
        await Assert.That(grid.GetCell(3, 4).Letter).IsEqualTo('G');
    }

    [Test]
    public async Task TryPlaceWord_PlacesWordCorrectlyDown()
    {
        var grid = new CrosswordGrid(10, 10);
        var word = new Word("CAT", "Animal");

        var success = grid.TryPlaceWord(word, 2, 4, Direction.Down);

        await Assert.That(success).IsTrue();
        await Assert.That(word.Direction).IsEqualTo(Direction.Down);

        await Assert.That(grid.GetCell(2, 4).Letter).IsEqualTo('C');
        await Assert.That(grid.GetCell(3, 4).Letter).IsEqualTo('A');
        await Assert.That(grid.GetCell(4, 4).Letter).IsEqualTo('T');
    }

    [Test]
    public async Task TryPlaceWord_FailsWhenWordExceedsGridWidth()
    {
        var grid = new CrosswordGrid(5, 5);
        var word = new Word("TOOLONG", "Too big");

        var success = grid.TryPlaceWord(word, 0, 0, Direction.Across);

        await Assert.That(success).IsFalse();
        await Assert.That(word.IsPlaced).IsFalse();
    }

    [Test]
    public async Task TryPlaceWord_FailsWhenWordExceedsGridHeight()
    {
        var grid = new CrosswordGrid(5, 5);
        var word = new Word("TOOLONG", "Too big");

        var success = grid.TryPlaceWord(word, 0, 0, Direction.Down);

        await Assert.That(success).IsFalse();
        await Assert.That(word.IsPlaced).IsFalse();
    }

    [Test]
    public async Task TryPlaceWord_AddsWordToCollection()
    {
        var grid = new CrosswordGrid(10, 10);
        var word = new Word("TEST", "A test");

        grid.TryPlaceWord(word, 0, 0, Direction.Across);

        await Assert.That(grid.Words.Count).IsEqualTo(1);
        await Assert.That(grid.Words).Contains(word);
    }

    [Test]
    public async Task CanPlaceWord_ReturnsTrueForValidPlacement()
    {
        var grid = new CrosswordGrid(10, 10);
        var word = new Word("TEST", "A test");

        var canPlace = grid.CanPlaceWord(word, 0, 0, Direction.Across);

        await Assert.That(canPlace).IsTrue();
    }

    [Test]
    public async Task CanPlaceWord_ReturnsFalseWhenOutOfBounds()
    {
        var grid = new CrosswordGrid(5, 5);
        var word = new Word("TOOLONG", "Too big");

        var canPlace = grid.CanPlaceWord(word, 0, 0, Direction.Across);

        await Assert.That(canPlace).IsFalse();
    }

    [Test]
    public async Task GetStats_ReturnsCorrectStatistics()
    {
        var grid = new CrosswordGrid(4, 4);
        var word = new Word("CAT", "Pet");
        grid.TryPlaceWord(word, 1, 1, Direction.Across);

        var stats = grid.GetStats();

        await Assert.That(stats.TotalCells).IsEqualTo(16);
        await Assert.That(stats.FilledCells).IsEqualTo(3);
        await Assert.That(stats.BlockedCells).IsEqualTo(0);
        await Assert.That(stats.EmptyCells).IsEqualTo(13);
        await Assert.That(stats.WordCount).IsEqualTo(1);
    }

    [Test]
    public async Task GetStats_CalculatesFillPercentage()
    {
        var grid = new CrosswordGrid(4, 4);
        var word = new Word("CAT", "Pet");
        grid.TryPlaceWord(word, 1, 1, Direction.Across);

        var stats = grid.GetStats();

        await Assert.That(stats.FillPercentage).IsEqualTo(18.75).Within(0.01);
    }

    [Test]
    public async Task GetPlacedWordTexts_ReturnsAllPlacedWords()
    {
        var grid = new CrosswordGrid(10, 10);
        grid.TryPlaceWord(new Word("CAT", "Animal"), 0, 0, Direction.Across);
        grid.TryPlaceWord(new Word("DOG", "Pet"), 2, 0, Direction.Across);

        var placedTexts = grid.GetPlacedWordTexts();

        await Assert.That(placedTexts.Count).IsEqualTo(2);
        await Assert.That(placedTexts).Contains("CAT");
        await Assert.That(placedTexts).Contains("DOG");
    }

    [Test]
    public async Task GetWordsByDirection_SeparatesAcrossAndDown()
    {
        var grid = new CrosswordGrid(10, 10);
        grid.TryPlaceWord(new Word("HEJ", "Hälsning"), 0, 0, Direction.Across);
        grid.TryPlaceWord(new Word("HUND", "Djur"), 0, 0, Direction.Down);

        var (across, down) = grid.GetWordsByDirection();

        await Assert.That(across.Count).IsEqualTo(1);
        await Assert.That(down.Count).IsEqualTo(1);
        await Assert.That(across[0].Text).IsEqualTo("HEJ");
        await Assert.That(down[0].Text).IsEqualTo("HUND");
    }

    [Test]
    public async Task RenumberClues_AssignsSequentialNumbers()
    {
        var grid = new CrosswordGrid(10, 10);
        var word1 = new Word("AB", "First");
        var word2 = new Word("CD", "Second");
        
        grid.TryPlaceWord(word1, 0, 0, Direction.Across);
        grid.TryPlaceWord(word2, 2, 0, Direction.Across);
        
        grid.RenumberClues();

        await Assert.That(word1.Number).IsEqualTo(1);
        await Assert.That(word2.Number).IsEqualTo(2);
    }

    [Test]
    public async Task RemoveWord_RemovesWordFromGrid()
    {
        var grid = new CrosswordGrid(10, 10);
        var word = new Word("TEST", "A test");
        grid.TryPlaceWord(word, 0, 0, Direction.Across);

        var removed = grid.RemoveWord(word);

        await Assert.That(removed).IsTrue();
        await Assert.That(grid.Words.Count).IsEqualTo(0);
        await Assert.That(word.IsPlaced).IsFalse();
    }

    [Test]
    public async Task FillEmptyCellsWithAsterisks_MarksEmptyCells()
    {
        var grid = new CrosswordGrid(3, 3);
        var word = new Word("AB", "Test");
        grid.TryPlaceWord(word, 0, 0, Direction.Across);

        grid.FillEmptyCellsWithAsterisks();

        await Assert.That(grid.GetCell(0, 0).Letter).IsEqualTo('A');
        await Assert.That(grid.GetCell(0, 1).Letter).IsEqualTo('B');
        await Assert.That(grid.GetCell(1, 0).Letter).IsEqualTo('*');
        await Assert.That(grid.GetCell(2, 2).Letter).IsEqualTo('*');
    }

    /// <summary>
    /// Regression test for the HALSA/RASAR bug:
    /// A straight word that would place a new letter immediately after the tail of an
    /// existing bent word's last segment (in the same direction) must be rejected.
    ///
    /// Grid layout (col 0-6, row 0-3):
    ///   col:  0 1 2 3 4 5 6
    ///   row0: H . . . . . .
    ///   row1: A . . . . . .
    ///   row2: L . . . . . .
    ///   row3: S A . . . . .   ? HALSA bends right at S; tail ends at (3,1)=A
    ///
    /// RASAR goes across on row 3: R(3,0–wait, S is there)—
    /// use columns 0-4: R(3,-1) would be out of range, so use col 1-5:
    ///   _ S A R ? -- no, S is at col 0. Use SARAS: S(3,0) A(3,1) R(3,2) A(3,3) S(3,4).
    /// The R at (3,2) is a new empty cell immediately after HALSA's tail A at (3,1) ? must be rejected.
    /// </summary>
    [Test]
    public async Task CanPlaceWord_ReturnsFalse_WhenNewLetterImmediatelyFollowsBentWordTail()
    {
        // 7-wide, 5-tall grid gives enough room
        var grid = new CrosswordGrid(7, 5);

        // Place HALS going Down at col 0, rows 0-3
        var hals = new Word("HALS", "Test");
        var placed = grid.TryPlaceWord(hals, 0, 0, Direction.Down);
        await Assert.That(placed).IsTrue();

        // Place the bent word HALSA: first segment Down (rows 0-3, col 0), second Across (row 3, cols 0-1)
        var halsa = new Word("HALSA", "Test");
        var segments = new List<WordSegment>
        {
            new WordSegment { StartRow = 0, StartCol = 0, Direction = Direction.Down,   Length = 4 }, // H A L S
            new WordSegment { StartRow = 3, StartCol = 0, Direction = Direction.Across, Length = 2 }, // S A  (S shared)
        };
        var bentPlaced = grid.TryPlaceBentWord(halsa, segments);
        await Assert.That(bentPlaced).IsTrue();
        // HALSA's last segment tail is now A at (3,1)

        // Try to place SARAS across on row 3 starting at col 0:  S(0) A(1) R(2) A(3) S(4)
        // S and A at cols 0-1 already exist; R at col 2 is a new cell immediately after the tail ? must be rejected
        var saras = new Word("SARAS", "Test");
        var canPlace = grid.CanPlaceWord(saras, 3, 0, Direction.Across);
        await Assert.That(canPlace).IsFalse();
    }

    /// <summary>
    /// Ensures a straight word that does NOT extend past a bent word's tail is still accepted.
    /// </summary>
    [Test]
    public async Task CanPlaceWord_ReturnsTrue_WhenStraightWordEndsAtBentWordTail()
    {
        var grid = new CrosswordGrid(7, 5);

        var hals = new Word("HALS", "Test");
        grid.TryPlaceWord(hals, 0, 0, Direction.Down);

        var halsa = new Word("HALSA", "Test");
        var segments = new List<WordSegment>
        {
            new WordSegment { StartRow = 0, StartCol = 0, Direction = Direction.Down,   Length = 4 },
            new WordSegment { StartRow = 3, StartCol = 0, Direction = Direction.Across, Length = 2 },
        };
        grid.TryPlaceBentWord(halsa, segments);

        // A two-letter word SA that exactly covers the bent word's tail cells should be allowed
        // because both cells already carry letters (S and A) — no new empty cell is added after the tail.
        var sa = new Word("SA", "Test");
        var canPlace = grid.CanPlaceWord(sa, 3, 0, Direction.Across);
        // SA shares all its cells with existing letters, so it merely overlaps — whether it's
        // allowed depends on other isolation rules, but it must NOT be blocked by our new check.
        // (The word-isolation check may still reject it for other reasons; we only verify the
        //  bent-tail check does not incorrectly block it.)
        // The important assertion is that it was NOT rejected solely due to WouldFollowBentWordTail.
        // We verify by confirming that a word ending exactly at the tail (col 1) is not rejected
        // by the new rule — there is no new empty cell after the tail.
        await Assert.That(canPlace).IsTrue();
    }

    // -------------------------------------------------------------------------
    // WouldFollowBentWordTail — Down-direction variant
    // -------------------------------------------------------------------------

    /// <summary>
    /// Same principle as the Across variant above, but with the bent word's last segment
    /// travelling Down.
    ///
    /// Grid (5x5):
    ///   col:  0 1 2 3 4
    ///   row0: A B C . .   ? ABCD first segment (Across, row 0, cols 0-2)
    ///   row1: . . D . .   ? ABCD last segment tail (Down, col 2); tail cell = D(1,2)
    ///   row2: . . X . .   ? X is the illegal new letter for "CDX" Down
    ///
    /// "CDX" Down reuses the existing C and D, then tries to place X at (2,2) —
    /// immediately below the Down tail D(1,2) ? must be rejected.
    /// </summary>
    [Test]
    public async Task CanPlaceWord_ReturnsFalse_WhenNewDownLetterImmediatelyFollowsDownBentWordTail()
    {
        var grid = new CrosswordGrid(5, 5);

        // Place bent "ABCD": first segment Across (row 0, cols 0-2), second segment Down (col 2, rows 0-1)
        var abcd = new Word("ABCD", "Test");
        var segments = new List<WordSegment>
        {
            new WordSegment { StartRow = 0, StartCol = 0, Direction = Direction.Across, Length = 3 }, // A B C
            new WordSegment { StartRow = 0, StartCol = 2, Direction = Direction.Down,   Length = 2 }, // C D  (C shared)
        };
        await Assert.That(grid.TryPlaceBentWord(abcd, segments)).IsTrue();
        // ABCD's last segment tail: D at (1,2) going Down

        // "CDX" Down starting at (0,2): C and D already exist; X at (2,2) is a new empty cell
        // immediately below the Down tail D(1,2) ? must be rejected
        var cdx = new Word("CDX", "Test");
        await Assert.That(grid.CanPlaceWord(cdx, 0, 2, Direction.Down)).IsFalse();
    }

    // -------------------------------------------------------------------------
    // TryPlaceWord propagation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that TryPlaceWord (not just CanPlaceWord) is also blocked when a new
    /// letter would land immediately after a bent word's Across tail.
    /// </summary>
    [Test]
    public async Task TryPlaceWord_ReturnsFalse_WhenNewLetterImmediatelyFollowsBentWordTail()
    {
        var grid = new CrosswordGrid(7, 5);

        var hals = new Word("HALS", "Test");
        grid.TryPlaceWord(hals, 0, 0, Direction.Down);

        var halsa = new Word("HALSA", "Test");
        var segments = new List<WordSegment>
        {
            new WordSegment { StartRow = 0, StartCol = 0, Direction = Direction.Down,   Length = 4 },
            new WordSegment { StartRow = 3, StartCol = 0, Direction = Direction.Across, Length = 2 },
        };
        grid.TryPlaceBentWord(halsa, segments);
        // HALSA's last segment tail: A at (3,1)

        // SARAS Across at row 3, cols 0-4: S(0) and A(1) exist; R at (2) is new, after the tail
        var saras = new Word("SARAS", "Test");
        var placed = grid.TryPlaceWord(saras, 3, 0, Direction.Across);

        await Assert.That(placed).IsFalse();
        await Assert.That(saras.IsPlaced).IsFalse();
    }

    // -------------------------------------------------------------------------
    // CanPlaceBentWord — tail-adjacency checks
    // -------------------------------------------------------------------------

    /// <summary>
    /// A new bent word whose last segment places an empty cell immediately after the
    /// Across tail of an existing bent word must be rejected.
    ///
    /// Grid (5x5):
    ///   col:  0 1 2 3 4
    ///   row0: A X . . .   ? ABCD seg1 Down (col 0); XYDR seg1 Down (col 1)
    ///   row1: B Y . . .
    ///   row2: C D R . .   ? ABCD Across tail = D(2,1); R at (2,2) follows it ? reject
    ///
    /// "XYDR": seg1 Down col 1 rows 0-2 (X, Y, D — D matches existing D), seg2 Across
    /// row 2 cols 1-2 (D shared, R new). R(2,2) is immediately after ABCD's tail ? rejected.
    /// </summary>
    [Test]
    public async Task CanPlaceBentWord_ReturnsFalse_WhenNewBentWordLastSegmentFollowsExistingBentWordTail()
    {
        var grid = new CrosswordGrid(5, 5);

        // Place bent "ABCD": Down col 0 rows 0-2 (A, B, C) then Across row 2 cols 0-1 (C shared, D)
        var abcd = new Word("ABCD", "Test");
        var segsAbcd = new List<WordSegment>
        {
            new WordSegment { StartRow = 0, StartCol = 0, Direction = Direction.Down,   Length = 3 }, // A B C
            new WordSegment { StartRow = 2, StartCol = 0, Direction = Direction.Across, Length = 2 }, // C D  (C shared)
        };
        await Assert.That(grid.TryPlaceBentWord(abcd, segsAbcd)).IsTrue();
        // ABCD Across tail: D at (2,1)

        // Try to place bent "XYDR": Down col 1 rows 0-2 (X, Y, D) then Across row 2 cols 1-2 (D shared, R)
        // D at (2,1) matches; R at (2,2) is an empty cell immediately after the Across tail D(2,1) ? rejected
        var xydr = new Word("XYDR", "Test");
        var segsXydr = new List<WordSegment>
        {
            new WordSegment { StartRow = 0, StartCol = 1, Direction = Direction.Down,   Length = 3 }, // X Y D
            new WordSegment { StartRow = 2, StartCol = 1, Direction = Direction.Across, Length = 2 }, // D R  (D shared)
        };
        await Assert.That(grid.CanPlaceBentWord(xydr, segsXydr)).IsFalse();
    }

    /// <summary>
    /// A new bent word positioned entirely away from any existing bent word's tail
    /// must not be blocked by WouldFollowBentWordTail.
    ///
    /// Grid (5x5):
    ///   col:  0 1 2 3 4
    ///   row0: A . . X .   ? ABCD seg1 Down (col 0); XYZW seg1 Down (col 3)
    ///   row1: B . . Y .
    ///   row2: C D . Z W   ? ABCD Across tail = D(2,1); XYZW Across tail = W(2,4) — no conflict
    ///
    /// "XYZW": seg1 Down col 3 rows 0-2, seg2 Across row 2 cols 3-4. W(2,4) follows Z(2,3),
    /// which is not the tail of any existing bent word ? accepted.
    /// </summary>
    [Test]
    public async Task CanPlaceBentWord_ReturnsTrue_WhenNewBentWordDoesNotFollowAnyBentWordTail()
    {
        var grid = new CrosswordGrid(5, 5);

        var abcd = new Word("ABCD", "Test");
        var segsAbcd = new List<WordSegment>
        {
            new WordSegment { StartRow = 0, StartCol = 0, Direction = Direction.Down,   Length = 3 },
            new WordSegment { StartRow = 2, StartCol = 0, Direction = Direction.Across, Length = 2 },
        };
        grid.TryPlaceBentWord(abcd, segsAbcd);
        // ABCD Across tail: D at (2,1)

        // "XYZW" is completely to the right; W(2,4) follows Z(2,3), which is not any tail
        var xyzw = new Word("XYZW", "Test");
        var segsXyzw = new List<WordSegment>
        {
            new WordSegment { StartRow = 0, StartCol = 3, Direction = Direction.Down,   Length = 3 }, // X Y Z
            new WordSegment { StartRow = 2, StartCol = 3, Direction = Direction.Across, Length = 2 }, // Z W  (Z shared)
        };
        await Assert.That(grid.CanPlaceBentWord(xyzw, segsXyzw)).IsTrue();
    }
}
