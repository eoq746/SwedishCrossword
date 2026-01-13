using TUnit.Assertions;
using TUnit.Core;

namespace SwedishCrossword.Tests;

/// <summary>
/// Simple test to verify TUnit is working correctly
/// </summary>
public class TUnitVerificationTests
{
    [Test]
    public async Task Simple_test_should_pass()
    {
        // Arrange
        var value = 42;
        var condition = value == 42;
        var text = "hello";

        // Act & Assert
        await Assert.That(value).IsEqualTo(42);
        await Assert.That(condition).IsTrue();
        await Assert.That(text).IsEqualTo("hello");
    }

    [Test]
    public async Task Another_simple_test_should_pass()
    {
        // Arrange
        var list = new List<int> { 1, 2, 3 };

        // Act & Assert
        await Assert.That(list.Count).IsEqualTo(3);
        await Assert.That(list).Contains(2);
        await Assert.That(list.First()).IsEqualTo(1);
    }
}