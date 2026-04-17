using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SwedishCrossword.Api.Tests;

public class SubmissionTokenServiceTests
{
    private SubmissionTokenService CreateService(string secret = "test-secret")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SubmissionToken:Secret"] = secret
            })
            .Build();
        return new SubmissionTokenService(config, NullLogger<SubmissionTokenService>.Instance, TimeProvider.System);
    }

    // -----------------------------------------------------------------------
    // ValidateAccess — HMAC + expiry only
    // -----------------------------------------------------------------------

    [Test]
    public async Task ValidateAccess_ValidToken_ReturnsValid()
    {
        var service = CreateService();
        var token = service.GenerateToken("hash123", 42);

        var result = service.ValidateAccess(token);

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task ValidateAccess_TamperedToken_ReturnsInvalid()
    {
        var fakeToken = Convert.ToBase64String(Encoding.UTF8.GetBytes("hash:10:0:badhmac"));

        var service = CreateService();
        var result = service.ValidateAccess(fakeToken);

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task ValidateAccess_MalformedToken_ReturnsInvalid()
    {
        var service = CreateService();
        var result = service.ValidateAccess("not-base64-at-all!!!");

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task ValidateAccess_WrongNumberOfParts_ReturnsInvalid()
    {
        var service = CreateService();
        var badToken = Convert.ToBase64String(Encoding.UTF8.GetBytes("only:two"));
        var result = service.ValidateAccess(badToken);

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task ValidateAccess_DifferentSecret_ReturnsInvalid()
    {
        var service1 = CreateService("secret-a");
        var token = service1.GenerateToken("hash", 10);

        var service2 = CreateService("secret-b");
        var result = service2.ValidateAccess(token);

        await Assert.That(result.IsValid).IsFalse();
    }

    // -----------------------------------------------------------------------
    // StripAnswers — removes letter from cells and answer from clues
    // -----------------------------------------------------------------------

    [Test]
    public async Task StripAnswers_RemovesLettersFromCells()
    {
        var json = JsonNode.Parse("""
            {
                "cells": [[{"letter":"A","num":1},null],[{"letter":"B"},null]],
                "clues": {"across":[],"down":[]}
            }
        """)!;

        SubmissionTokenService.StripAnswers((JsonObject)json);

        var cells = json["cells"]!.AsArray();
        var cell00 = cells[0]!.AsArray()[0]!.AsObject();
        await Assert.That(cell00.ContainsKey("letter")).IsFalse();
        // Other properties should be preserved
        await Assert.That(cell00.ContainsKey("num")).IsTrue();
    }

    [Test]
    public async Task StripAnswers_RemovesAnswersFromClues()
    {
        var json = JsonNode.Parse("""
            {
                "cells": [],
                "clues": {
                    "across": [{"number":1,"clue":"Test","answer":"ABC"}],
                    "down": [{"number":2,"clue":"More","answer":"XY"}]
                }
            }
        """)!;

        SubmissionTokenService.StripAnswers((JsonObject)json);

        var across = json["clues"]!["across"]!.AsArray();
        await Assert.That(across[0]!.AsObject().ContainsKey("answer")).IsFalse();
        await Assert.That(across[0]!.AsObject().ContainsKey("clue")).IsTrue();

        var down = json["clues"]!["down"]!.AsArray();
        await Assert.That(down[0]!.AsObject().ContainsKey("answer")).IsFalse();
    }

    [Test]
    public async Task StripAnswers_HandlesEmptyCellsArray()
    {
        var json = JsonNode.Parse("""
            {
                "cells": [],
                "clues": {"across":[],"down":[]}
            }
        """)!;

        // Should not throw
        SubmissionTokenService.StripAnswers((JsonObject)json);
        await Assert.That(json["cells"]!.AsArray().Count).IsEqualTo(0);
    }

    // -----------------------------------------------------------------------
    // ReadAnswers — reads answer map from puzzle file on disk
    // -----------------------------------------------------------------------

    [Test]
    public async Task ReadAnswers_ValidFile_ReturnsDictionary()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, """
                {
                    "cells": [
                        [{"letter":"K"},{"letter":"A"},null],
                        [null,{"letter":"B"},null]
                    ]
                }
            """);

            var answers = await SubmissionTokenService.ReadAnswersAsync(tempFile);

            await Assert.That(answers).IsNotNull();
            await Assert.That(answers!.Count).IsEqualTo(3);
            await Assert.That(answers["0,0"]).IsEqualTo("K");
            await Assert.That(answers["0,1"]).IsEqualTo("A");
            await Assert.That(answers["1,1"]).IsEqualTo("B");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task ReadAnswers_MissingFile_ReturnsNull()
    {
        var result = await SubmissionTokenService.ReadAnswersAsync("/nonexistent/path.json");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReadAnswers_InvalidJson_ReturnsNull()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "not json at all");
            var result = await SubmissionTokenService.ReadAnswersAsync(tempFile);
            await Assert.That(result).IsNull();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task ReadAnswers_NoCellsProperty_ReturnsNull()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, """{"width":3}""");
            var result = await SubmissionTokenService.ReadAnswersAsync(tempFile);
            await Assert.That(result).IsNull();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task ReadAnswers_SkipsNullCells()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, """
                {
                    "cells": [[null,{"letter":"X"},null]]
                }
            """);

            var answers = await SubmissionTokenService.ReadAnswersAsync(tempFile);

            await Assert.That(answers).IsNotNull();
            await Assert.That(answers!.Count).IsEqualTo(1);
            await Assert.That(answers.ContainsKey("0,0")).IsFalse();
            await Assert.That(answers["0,1"]).IsEqualTo("X");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // -----------------------------------------------------------------------
    // ComputePuzzleMetadata — hash and cell count
    // -----------------------------------------------------------------------

    [Test]
    public async Task ComputePuzzleMetadata_CountsNonNullCells()
    {
        var json = JsonNode.Parse("""
            {
                "width": 3,
                "cells": [
                    [{"letter":"A"},null,{"letter":"B"}],
                    [null,{"letter":"C"},null]
                ]
            }
        """)!;

        var (_, cellCount) = SubmissionTokenService.ComputePuzzleMetadata((JsonObject)json);

        await Assert.That(cellCount).IsEqualTo(3);
    }

    [Test]
    public async Task ComputePuzzleMetadata_ProducesDeterministicHash()
    {
        var json1 = JsonNode.Parse("""
            {"width":2,"cells":[[{"letter":"A"},{"letter":"B"}],[null,{"letter":"C"}]]}
        """)!;
        var json2 = JsonNode.Parse("""
            {"width":2,"cells":[[{"letter":"A"},{"letter":"B"}],[null,{"letter":"C"}]]}
        """)!;

        var (hash1, _) = SubmissionTokenService.ComputePuzzleMetadata((JsonObject)json1);
        var (hash2, _) = SubmissionTokenService.ComputePuzzleMetadata((JsonObject)json2);

        await Assert.That(hash1).IsEqualTo(hash2);
    }

    // -----------------------------------------------------------------------
    // InjectToken — strips answers and adds metadata
    // -----------------------------------------------------------------------

    [Test]
    public async Task InjectToken_AddsTokenAndStripsAnswers()
    {
        var service = CreateService();
        var puzzleJson = """
            {
                "width":2,"height":2,
                "cells":[[{"letter":"A"},{"letter":"B"}],[null,null]],
                "clues":{"across":[{"number":1,"clue":"Test","answer":"AB"}],"down":[]}
            }
        """;

        var result = service.InjectToken(puzzleJson, DateOnly.FromDateTime(DateTime.UtcNow));
        var obj = JsonNode.Parse(result)!.AsObject();

        // Should have token and metadata
        await Assert.That(obj.ContainsKey("submissionToken")).IsTrue();
        await Assert.That(obj.ContainsKey("puzzleHash")).IsTrue();
        await Assert.That(obj.ContainsKey("cellCount")).IsTrue();
        await Assert.That(obj.ContainsKey("puzzleDate")).IsTrue();

        // Letters should be stripped from cells
        var cell = obj["cells"]![0]![0]!.AsObject();
        await Assert.That(cell.ContainsKey("letter")).IsFalse();

        // Answers should be stripped from clues
        var clue = obj["clues"]!["across"]![0]!.AsObject();
        await Assert.That(clue.ContainsKey("answer")).IsFalse();
        await Assert.That(clue.ContainsKey("clue")).IsTrue();
    }
}
