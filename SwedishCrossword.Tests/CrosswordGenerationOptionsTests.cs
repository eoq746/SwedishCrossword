using TUnit.Assertions;
using TUnit.Core;
using SwedishCrossword.Models;
using SwedishCrossword.Services;

namespace SwedishCrossword.Tests;

/// <summary>
/// Tests for <see cref="CrosswordGenerationOptions"/> preset configurations and computed properties.
/// </summary>
public class CrosswordGenerationOptionsTests
{
    // -----------------------------------------------------------------------
    // Preset configurations
    // -----------------------------------------------------------------------

    [Test]
    public async Task Easy_HasExpectedDimensions()
    {
        var opts = CrosswordGenerationOptions.Easy;

        await Assert.That(opts.Width).IsEqualTo(11);
        await Assert.That(opts.Height).IsEqualTo(11);
    }

    [Test]
    public async Task Medium_HasExpectedDimensions()
    {
        var opts = CrosswordGenerationOptions.Medium;

        await Assert.That(opts.Width).IsEqualTo(15);
        await Assert.That(opts.Height).IsEqualTo(15);
    }

    [Test]
    public async Task Hard_HasExpectedDimensions()
    {
        var opts = CrosswordGenerationOptions.Hard;

        await Assert.That(opts.Width).IsEqualTo(17);
        await Assert.That(opts.Height).IsEqualTo(17);
    }

    [Test]
    public async Task Small_HasExpectedDimensions()
    {
        var opts = CrosswordGenerationOptions.Small;

        await Assert.That(opts.Width).IsEqualTo(9);
        await Assert.That(opts.Height).IsEqualTo(9);
    }

    [Test]
    public async Task Mobile_HasExpectedDimensions()
    {
        var opts = CrosswordGenerationOptions.Mobile;

        await Assert.That(opts.Width).IsEqualTo(10);
        await Assert.That(opts.Height).IsEqualTo(10);
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
