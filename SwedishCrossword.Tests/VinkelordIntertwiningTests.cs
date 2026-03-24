using TUnit.Assertions;
using TUnit.Core;
using SwedishCrossword.Models;
using SwedishCrossword.Services;

namespace SwedishCrossword.Tests;

/// <summary>
/// Tests for the fixes that prevent intertwined vinkelord from producing
/// visual double-bends (Issue 2) and missing accidental-word clues (Issue 1).
/// </summary>
public class VinkelordIntertwiningTests
{
    #region Issue 2 — CanPlaceBentWord rejects overlapping bend arrows

    /// <summary>
    /// Two vinkelord must not share a bend cell.
    /// Vinkelord A bends at (3,5) with arrow Down.
    /// Vinkelord B also wants to bend at (3,5) — must be rejected because
    /// overwriting the arrow makes them look like one word with two bends.
    ///
    /// Grid (10×10):
    ///   A bends Across→Down at (3,5).
    ///   B bends Down→Across at (3,5) — same cell.
    /// </summary>
    [Test]
    public async Task CanPlaceBentWord_RejectsBendCellWithExistingBendArrow()
    {
        var grid = new CrosswordGrid(10, 10);

        // Place vinkelord A: Across (3,3)→(3,5), Down (3,5)→(5,5)
        var wordA = new Word("ABCDE", "First");
        var segsA = new List<WordSegment>
        {
            new() { StartRow = 3, StartCol = 3, Direction = Direction.Across, Length = 3 }, // A B C
            new() { StartRow = 3, StartCol = 5, Direction = Direction.Down,   Length = 3 }  // C D E
        };
        var placedA = grid.TryPlaceBentWordWithValidation(wordA, segsA);
        await Assert.That(placedA).IsTrue();
        await Assert.That(grid.GetCell(3, 5).BendArrowDirection).IsEqualTo(Direction.Down);

        // Try vinkelord B: Down (1,5)→(3,5), Across (3,5)→(3,7)
        // Its bend cell is also (3,5) — already has an arrow.
        var wordB = new Word("XYCDE", "Second");
        var segsB = new List<WordSegment>
        {
            new() { StartRow = 1, StartCol = 5, Direction = Direction.Down,   Length = 3 }, // X Y C
            new() { StartRow = 3, StartCol = 5, Direction = Direction.Across, Length = 3 }  // C D E
        };
        var canPlaceB = grid.CanPlaceBentWord(wordB, segsB);

        await Assert.That(canPlaceB).IsFalse();
    }

    /// <summary>
    /// A vinkelord whose start cell already carries a BendArrowDirection must be
    /// rejected. If it were allowed, the reader would see the existing arrow and
    /// the new word as one continuous path — a visual double-bend.
    ///
    /// Grid (10×10):
    ///   A bends Across→Down at (3,5), arrow Down.
    ///   B starts at (3,5) going Down then bends Across at (5,5).
    ///   Cell (3,5) already has an arrow → B must be rejected.
    /// </summary>
    [Test]
    public async Task CanPlaceBentWord_RejectsStartCellWithExistingBendArrow()
    {
        var grid = new CrosswordGrid(10, 10);

        // Place vinkelord A: Across (3,3)→(3,5), Down (3,5)→(5,5)
        var wordA = new Word("ABCDE", "First");
        var segsA = new List<WordSegment>
        {
            new() { StartRow = 3, StartCol = 3, Direction = Direction.Across, Length = 3 },
            new() { StartRow = 3, StartCol = 5, Direction = Direction.Down,   Length = 3 }
        };
        await Assert.That(grid.TryPlaceBentWordWithValidation(wordA, segsA)).IsTrue();

        // Try vinkelord B starting at (3,5) — cell already carries an arrow.
        // Down (3,5)→(5,5), Across (5,5)→(5,7)
        var wordB = new Word("CDEFG", "Second");
        var segsB = new List<WordSegment>
        {
            new() { StartRow = 3, StartCol = 5, Direction = Direction.Down,   Length = 3 }, // C D E (start = bend cell of A)
            new() { StartRow = 5, StartCol = 5, Direction = Direction.Across, Length = 3 }  // E F G
        };
        var canPlaceB = grid.CanPlaceBentWord(wordB, segsB);

        await Assert.That(canPlaceB).IsFalse();
    }

    /// <summary>
    /// Two vinkelord that do NOT share bend cells and whose start/end cells do not
    /// overlap with any existing BendArrowDirection must both be placeable.
    /// </summary>
    [Test]
    public async Task CanPlaceBentWord_AllowsSeparateBendCells()
    {
        var grid = new CrosswordGrid(10, 10);

        // Place vinkelord A: Across (1,1)→(1,3), Down (1,3)→(3,3)
        var wordA = new Word("ABCDE", "First");
        var segsA = new List<WordSegment>
        {
            new() { StartRow = 1, StartCol = 1, Direction = Direction.Across, Length = 3 },
            new() { StartRow = 1, StartCol = 3, Direction = Direction.Down,   Length = 3 }
        };
        await Assert.That(grid.TryPlaceBentWordWithValidation(wordA, segsA)).IsTrue();

        // Vinkelord B well away: Across (6,1)→(6,3), Down (6,3)→(8,3)
        var wordB = new Word("FGHIJ", "Second");
        var segsB = new List<WordSegment>
        {
            new() { StartRow = 6, StartCol = 1, Direction = Direction.Across, Length = 3 },
            new() { StartRow = 6, StartCol = 3, Direction = Direction.Down,   Length = 3 }
        };
        var canPlaceB = grid.CanPlaceBentWord(wordB, segsB);

        await Assert.That(canPlaceB).IsTrue();
    }

    /// <summary>
    /// TryPlaceBentWordWithValidation must also reject the placement (not just
    /// CanPlaceBentWord) when bend cells overlap.
    /// </summary>
    [Test]
    public async Task TryPlaceBentWord_RejectsBendCellWithExistingBendArrow()
    {
        var grid = new CrosswordGrid(10, 10);

        var wordA = new Word("ABCDE", "First");
        var segsA = new List<WordSegment>
        {
            new() { StartRow = 3, StartCol = 3, Direction = Direction.Across, Length = 3 },
            new() { StartRow = 3, StartCol = 5, Direction = Direction.Down,   Length = 3 }
        };
        grid.TryPlaceBentWordWithValidation(wordA, segsA);

        var wordB = new Word("XYCDE", "Second");
        var segsB = new List<WordSegment>
        {
            new() { StartRow = 1, StartCol = 5, Direction = Direction.Down,   Length = 3 },
            new() { StartRow = 3, StartCol = 5, Direction = Direction.Across, Length = 3 }
        };
        var placed = grid.TryPlaceBentWordWithValidation(wordB, segsB);

        await Assert.That(placed).IsFalse();
        await Assert.That(wordB.IsPlaced).IsFalse();
    }

    /// <summary>
    /// After rejecting a second vinkelord that would overlap bend cells,
    /// the original bend arrow must not be corrupted.
    /// </summary>
    [Test]
    public async Task TryPlaceBentWord_PreservesExistingArrowOnRejection()
    {
        var grid = new CrosswordGrid(10, 10);

        var wordA = new Word("ABCDE", "First");
        var segsA = new List<WordSegment>
        {
            new() { StartRow = 3, StartCol = 3, Direction = Direction.Across, Length = 3 },
            new() { StartRow = 3, StartCol = 5, Direction = Direction.Down,   Length = 3 }
        };
        grid.TryPlaceBentWordWithValidation(wordA, segsA);

        var wordB = new Word("XYCDE", "Second");
        var segsB = new List<WordSegment>
        {
            new() { StartRow = 1, StartCol = 5, Direction = Direction.Down,   Length = 3 },
            new() { StartRow = 3, StartCol = 5, Direction = Direction.Across, Length = 3 }
        };
        grid.TryPlaceBentWordWithValidation(wordB, segsB);

        // Arrow at the bend cell must still point Down (from word A)
        await Assert.That(grid.GetCell(3, 5).BendArrowDirection).IsEqualTo(Direction.Down);
    }

    #endregion

    #region Issue 1 — DetectAccidentalWords at bend cells

    /// <summary>
    /// When a bend cell IS a word boundary (no adjacent letter in the second
    /// segment's direction), the first pass naturally detects the run starting
    /// at the bend cell. No special bend-cell handling is required.
    ///
    /// Grid (10×10):
    ///   Bent word "ABCDE": Down (1,5)→(3,5), Across (3,5)→(3,7)
    ///   No other word places a letter at (3,4) — so (3,5) IS a word start.
    /// </summary>
    [Test]
    public async Task DetectAccidentalWords_FirstPassFindsWordAtBendCellBoundary()
    {
        var grid = new CrosswordGrid(10, 10);

        var bent = new Word("ABCDE", "Bent");
        var segs = new List<WordSegment>
        {
            new() { StartRow = 1, StartCol = 5, Direction = Direction.Down,   Length = 3 },
            new() { StartRow = 3, StartCol = 5, Direction = Direction.Across, Length = 3 }
        };
        grid.TryPlaceBentWordWithValidation(bent, segs);

        var accidentalWords = grid.DetectAccidentalWords();

        var cdeWord = accidentalWords.FirstOrDefault(w =>
            w.StartRow == 3 && w.StartCol == 5 && w.Direction == Direction.Across && w.Text == "CDE");

        await Assert.That(cdeWord).IsNotNull();
    }

    /// <summary>
    /// When a letter is placed adjacent to a bend cell (making the bend cell
    /// mid-word in the second segment's direction), the sub-run starting at the
    /// bend cell must NOT be detected — it would be a fragment of the full run
    /// and may not be a valid word, causing false grid rejections.
    ///
    /// Grid (10×10):
    ///   Bent word "ABCDE": Down (1,5)→(3,5), Across (3,5)→(3,7)
    ///   Letter 'X' placed directly at (3,4) to simulate another word.
    ///
    /// The full run from (3,4) is "XCDE" — detected by the first pass.
    /// The sub-run "CDE" from (3,5) must NOT be detected (not a word boundary).
    /// </summary>
    [Test]
    public async Task DetectAccidentalWords_NoSubRunAtNonBoundaryBendCell()
    {
        var grid = new CrosswordGrid(10, 10);

        var bent = new Word("ABCDE", "Bent");
        var segs = new List<WordSegment>
        {
            new() { StartRow = 1, StartCol = 5, Direction = Direction.Down,   Length = 3 },
            new() { StartRow = 3, StartCol = 5, Direction = Direction.Across, Length = 3 }
        };
        grid.TryPlaceBentWordWithValidation(bent, segs);

        // Directly place a letter at (3,4), adjacent to bend cell, to make it mid-word
        grid.GetCell(3, 4).SetLetter('X', "simulated");

        var accidentalWords = grid.DetectAccidentalWords();

        // The full run "XCDE" from (3,4) IS detected
        var fullRun = accidentalWords.FirstOrDefault(w =>
            w.StartRow == 3 && w.StartCol == 4 && w.Direction == Direction.Across);
        await Assert.That(fullRun).IsNotNull();
        await Assert.That(fullRun!.Text).IsEqualTo("XCDE");

        // The sub-run "CDE" from (3,5) must NOT be detected
        var subRun = accidentalWords.FirstOrDefault(w =>
            w.StartRow == 3 && w.StartCol == 5 && w.Direction == Direction.Across && w.Text == "CDE");
        await Assert.That(subRun).IsNull();
    }

    /// <summary>
    /// Same as above but for the vertical direction: a letter above the bend cell
    /// makes it mid-word. Only the full vertical run is detected, not the sub-run.
    ///
    /// Grid (10×10):
    ///   Bent word "ABCDE": Across (3,1)→(3,3), Down (3,3)→(5,3)
    ///   Letter 'Y' placed directly at (2,3) to simulate another word.
    ///
    /// The full run from (2,3) is "YCDE" — detected.
    /// The sub-run "CDE" from (3,3) must NOT be detected.
    /// </summary>
    [Test]
    public async Task DetectAccidentalWords_NoSubRunAtNonBoundaryBendCellVertical()
    {
        var grid = new CrosswordGrid(10, 10);

        var bent = new Word("ABCDE", "Bent");
        var segs = new List<WordSegment>
        {
            new() { StartRow = 3, StartCol = 1, Direction = Direction.Across, Length = 3 },
            new() { StartRow = 3, StartCol = 3, Direction = Direction.Down,   Length = 3 }
        };
        grid.TryPlaceBentWordWithValidation(bent, segs);

        // Directly place a letter above bend cell to make it mid-word
        grid.GetCell(2, 3).SetLetter('Y', "simulated");

        var accidentalWords = grid.DetectAccidentalWords();

        // The full run "YCDE" from (2,3) IS detected
        var fullRun = accidentalWords.FirstOrDefault(w =>
            w.StartRow == 2 && w.StartCol == 3 && w.Direction == Direction.Down);
        await Assert.That(fullRun).IsNotNull();
        await Assert.That(fullRun!.Text).IsEqualTo("YCDE");

        // The sub-run "CDE" from (3,3) must NOT be detected
        var subRun = accidentalWords.FirstOrDefault(w =>
            w.StartRow == 3 && w.StartCol == 3 && w.Direction == Direction.Down && w.Text == "CDE");
        await Assert.That(subRun).IsNull();
    }

    #endregion

    #region Combined scenario — intertwined vinkelord prevented

    /// <summary>
    /// End-to-end scenario: two vinkelord that would create a visual Z-shape
    /// (sharing a bend cell) are prevented, so the grid only contains the first.
    /// </summary>
    [Test]
    public async Task InterleavedVinkelord_OnlyFirstPlaced()
    {
        var grid = new CrosswordGrid(10, 10);

        // Vinkelord A: Across→Down, bend at (3,5)
        var wordA = new Word("ABCDE", "First");
        var segsA = new List<WordSegment>
        {
            new() { StartRow = 3, StartCol = 3, Direction = Direction.Across, Length = 3 },
            new() { StartRow = 3, StartCol = 5, Direction = Direction.Down,   Length = 3 }
        };
        await Assert.That(grid.TryPlaceBentWordWithValidation(wordA, segsA)).IsTrue();

        // Vinkelord B: Down→Across, would bend at (3,5) — rejected
        var wordB = new Word("XYCFG", "Second");
        var segsB = new List<WordSegment>
        {
            new() { StartRow = 1, StartCol = 5, Direction = Direction.Down,   Length = 3 },
            new() { StartRow = 3, StartCol = 5, Direction = Direction.Across, Length = 3 }
        };
        await Assert.That(grid.TryPlaceBentWordWithValidation(wordB, segsB)).IsFalse();

        // Only one word on the grid
        await Assert.That(grid.Words.Count).IsEqualTo(1);
        await Assert.That(grid.Words[0].Text).IsEqualTo("ABCDE");

        // Bend arrow unchanged
        await Assert.That(grid.GetCell(3, 5).BendArrowDirection).IsEqualTo(Direction.Down);
    }

    /// <summary>
    /// Vinkelord that intersect (share a cell) but whose bend cells are distinct
    /// must still be allowed.
    ///
    /// Grid (10×10):
    ///   Vinkelord A: Across (2,1)→(2,3), Down (2,3)→(4,3) — bend at (2,3)
    ///     Cells: (2,1)A (2,2)B (2,3)C (3,3)D (4,3)E
    ///   Vinkelord B: Down (1,3)→(3,3), Across (3,3)→(3,5) — bend at (3,3)
    ///     Cells: (1,3)X (2,3)C (3,3)D (3,4)Y (3,5)Z
    ///   Shared cells: (2,3)=C and (3,3)=D. Bend cells (2,3) and (3,3) are different.
    /// </summary>
    [Test]
    public async Task IntersectingVinkelord_AllowedWhenBendCellsDistinct()
    {
        var grid = new CrosswordGrid(10, 10);

        // Vinkelord A: Across (2,1)→(2,3), Down (2,3)→(4,3) — bend at (2,3)
        var wordA = new Word("ABCDE", "First");
        var segsA = new List<WordSegment>
        {
            new() { StartRow = 2, StartCol = 1, Direction = Direction.Across, Length = 3 },
            new() { StartRow = 2, StartCol = 3, Direction = Direction.Down,   Length = 3 }
        };
        await Assert.That(grid.TryPlaceBentWordWithValidation(wordA, segsA)).IsTrue();

        // Vinkelord B: Down (1,3)→(3,3), Across (3,3)→(3,5) — bend at (3,3)
        // Shares cells (2,3)=C and (3,3)=D with word A; bend cells are different.
        var wordB = new Word("XCDYZ", "Second");
        var segsB = new List<WordSegment>
        {
            new() { StartRow = 1, StartCol = 3, Direction = Direction.Down,   Length = 3 },
            new() { StartRow = 3, StartCol = 3, Direction = Direction.Across, Length = 3 }
        };
        await Assert.That(grid.TryPlaceBentWordWithValidation(wordB, segsB)).IsTrue();

        await Assert.That(grid.Words.Count).IsEqualTo(2);
        await Assert.That(grid.GetCell(2, 3).BendArrowDirection).IsEqualTo(Direction.Down);
        await Assert.That(grid.GetCell(3, 3).BendArrowDirection).IsEqualTo(Direction.Across);
    }

    #endregion
}
