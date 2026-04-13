using System.Text.Json;
using TUnit.Assertions;
using TUnit.Core;
using SwedishCrossword.Services;

namespace SwedishCrossword.Tests;

/// <summary>
/// Tests for <see cref="SafeJsonEncoder"/> cached options and encoder behavior.
/// </summary>
public class SafeJsonEncoderTests
{
    [Test]
    public async Task Instance_IsNotNull()
    {
        await Assert.That(SafeJsonEncoder.Instance).IsNotNull();
    }

    [Test]
    public async Task DefaultOptions_HasWriteIndented()
    {
        await Assert.That(SafeJsonEncoder.DefaultOptions.WriteIndented).IsTrue();
    }

    [Test]
    public async Task DefaultOptions_HasEncoder()
    {
        await Assert.That(SafeJsonEncoder.DefaultOptions.Encoder).IsEqualTo(SafeJsonEncoder.Instance);
    }

    [Test]
    public async Task DeserializeOptions_HasPropertyNameCaseInsensitive()
    {
        await Assert.That(SafeJsonEncoder.DeserializeOptions.PropertyNameCaseInsensitive).IsTrue();
    }

    [Test]
    public async Task DeserializeOptions_HasEncoder()
    {
        await Assert.That(SafeJsonEncoder.DeserializeOptions.Encoder).IsEqualTo(SafeJsonEncoder.Instance);
    }

    [Test]
    public async Task DefaultOptions_ReturnsSameInstance()
    {
        var first = SafeJsonEncoder.DefaultOptions;
        var second = SafeJsonEncoder.DefaultOptions;

        await Assert.That(first).IsEqualTo(second);
    }

    [Test]
    public async Task DeserializeOptions_ReturnsSameInstance()
    {
        var first = SafeJsonEncoder.DeserializeOptions;
        var second = SafeJsonEncoder.DeserializeOptions;

        await Assert.That(first).IsEqualTo(second);
    }

    [Test]
    public async Task Encoder_PreservesSwedishCharacters()
    {
        var obj = new { word = "STÖRTLOPP" };
        var json = JsonSerializer.Serialize(obj, SafeJsonEncoder.DefaultOptions);

        await Assert.That(json).Contains("STÖRTLOPP");
    }

    [Test]
    public async Task Encoder_PreservesÅÄÖ()
    {
        var obj = new { text = "ÅÄÖ åäö" };
        var json = JsonSerializer.Serialize(obj, SafeJsonEncoder.DefaultOptions);

        await Assert.That(json).Contains("ÅÄÖ åäö");
    }

    [Test]
    public async Task DeserializeOptions_RoundTrips_CaseInsensitive()
    {
        var json = """{"Name":"test","VALUE":42}""";
        var result = JsonSerializer.Deserialize<TestDto>(json, SafeJsonEncoder.DeserializeOptions);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Name).IsEqualTo("test");
        await Assert.That(result.Value).IsEqualTo(42);
    }

    private record TestDto(string Name, int Value);
}
