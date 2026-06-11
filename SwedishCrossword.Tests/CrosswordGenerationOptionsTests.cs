using TUnit.Assertions;
using TUnit.Core;
using SwedishCrossword.Models;
using SwedishCrossword.Services;

namespace SwedishCrossword.Tests;

[Category("Unit")]
/// <summary>
/// Tests for <see cref="CrosswordGenerationOptions"/> preset configurations and computed properties.
/// </summary>
public class CrosswordGenerationOptionsTests
{
    // -----------------------------------------------------------------------
    // Preset configurations
    // -----------------------------------------------------------------------

    [Test, Category("Unit"), Category("Validation")]
    [Arguments("Easy", 11, 11, DisplayName = "Preset Easy dimensions")]
    [Arguments("Medium", 15, 15, DisplayName = "Preset Medium dimensions")]
    [Arguments("Hard", 17, 17, DisplayName = "Preset Hard dimensions")]
    [Arguments("Small", 9, 9, DisplayName = "Preset Small dimensions")]
    [Arguments("Mobile", 10, 10, DisplayName = "Preset Mobile dimensions")]
    public async Task Preset_HasExpectedDimensions(string presetName, int expectedWidth, int expectedHeight)
    {
        var opts = presetName switch
        {
            "Easy" => CrosswordGenerationOptions.Easy,
            "Medium" => CrosswordGenerationOptions.Medium,
            "Hard" => CrosswordGenerationOptions.Hard,
            "Small" => CrosswordGenerationOptions.Small,
            "Mobile" => CrosswordGenerationOptions.Mobile,
            _ => throw new ArgumentOutOfRangeException(nameof(presetName), presetName, null)
        };

        await Assert.That(opts.Width).IsEqualTo(expectedWidth);
        await Assert.That(opts.Height).IsEqualTo(expectedHeight);
    }

    [Test]
    public async Task Hard_AllowsVinkelord()
    {
        var opts = CrosswordGenerationOptions.Hard;

        await Assert.That(opts.AllowVinkelord).IsTrue();
    }

    [Test]
    public async Task Easy_RejectsInvalidWords()
    {
        var opts = CrosswordGenerationOptions.Easy;

        await Assert.That(opts.RejectInvalidWords).IsTrue();
        await Assert.That(opts.RejectDuplicateWords).IsTrue();
    }

    // -----------------------------------------------------------------------
    // Computed property: MaxVinkelordLength
    // -----------------------------------------------------------------------

    [Test]
    public async Task MaxVinkelordLength_ComputedFromDimensions()
    {
        var opts = new CrosswordGenerationOptions { Width = 10, Height = 12 };

        await Assert.That(opts.MaxVinkelordLength).IsEqualTo(21); // 10 + 12 - 1
    }

    [Test]
    public async Task MaxVinkelordLength_SymmetricGrid()
    {
        var opts = new CrosswordGenerationOptions { Width = 15, Height = 15 };

        await Assert.That(opts.MaxVinkelordLength).IsEqualTo(29);
    }

    // -----------------------------------------------------------------------
    // Default values
    // -----------------------------------------------------------------------

    [Test]
    public async Task Default_HasExpectedDefaults()
    {
        var opts = new CrosswordGenerationOptions();

        await Assert.That(opts.Width).IsEqualTo(15);
        await Assert.That(opts.Height).IsEqualTo(15);
        await Assert.That(opts.MinWordLength).IsEqualTo(1);
        await Assert.That(opts.MaxAttempts).IsEqualTo(100);
        await Assert.That(opts.RejectInvalidWords).IsTrue();
        await Assert.That(opts.RejectDuplicateWords).IsTrue();
        await Assert.That(opts.AllowVinkelord).IsTrue();
        await Assert.That(opts.MaxBendsPerWord).IsEqualTo(1);
    }

    [Test]
    public async Task Default_NullDifficulty()
    {
        var opts = new CrosswordGenerationOptions();

        await Assert.That(opts.Difficulty).IsNull();
    }

    [Test]
    public async Task Default_NullCategories()
    {
        var opts = new CrosswordGenerationOptions();

        await Assert.That(opts.Categories).IsNull();
    }

    // -----------------------------------------------------------------------
    // Presets increase in fill percentage
    // -----------------------------------------------------------------------

    [Test]
    public async Task Presets_FillPercentageIncreasesWithDifficulty()
    {
        var easy = CrosswordGenerationOptions.Easy.TargetFillPercentage;
        var medium = CrosswordGenerationOptions.Medium.TargetFillPercentage;
        var hard = CrosswordGenerationOptions.Hard.TargetFillPercentage;

        await Assert.That(easy).IsLessThan(medium);
        await Assert.That(medium).IsLessThan(hard);
    }
}
