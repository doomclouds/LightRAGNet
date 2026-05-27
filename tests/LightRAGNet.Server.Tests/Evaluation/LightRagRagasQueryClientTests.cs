using System.Text.Json;
using FluentAssertions;
using LightRAGNet.Core.Utils;
using LightRAGNet.Server.Services.Evaluation;

namespace LightRAGNet.Server.Tests.Evaluation;

public sealed class LightRagRagasQueryClientTests
{
    [Fact]
    public void ExtractContexts_WhenRawDataContainsDictionaryChunks_ReturnsContexts()
    {
        var rawData = new Dictionary<string, object>
        {
            ["data"] = new Dictionary<string, object>
            {
                ["chunks"] = new List<Dictionary<string, object>>
                {
                    new()
                    {
                        ["content"] = "First context",
                        ["chunk_id"] = "chunk-1",
                        ["file_path"] = "docs/first.md",
                        ["reference_id"] = "ref-1"
                    },
                    new()
                    {
                        ["content"] = "Second context",
                        ["chunk_id"] = "chunk-2",
                        ["file_path"] = "docs/second.md"
                    }
                }
            }
        };

        var contexts = LightRagRagasQueryClient.ExtractContexts(rawData);

        contexts.Should().Equal(
            new RagasRetrievedContext("First context", "chunk-1", "docs/first.md", "ref-1"),
            new RagasRetrievedContext("Second context", "chunk-2", "docs/second.md", string.Empty));
    }

    [Fact]
    public void ExtractContexts_WhenRawDataContainsJsonElementChunks_ReturnsContexts()
    {
        var json = JsonSerializer.Serialize(
            new
            {
                data = new
                {
                    chunks = new[]
                    {
                        new
                        {
                            content = "Json context",
                            chunk_id = "chunk-json",
                            file_path = "docs/json.md",
                            reference_id = "ref-json"
                        }
                    }
                }
            },
            LightRAGJsonOptions.HumanReadableCamelCaseWithStringEnums);
        var rawData = JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;

        var contexts = LightRagRagasQueryClient.ExtractContexts(rawData);

        contexts.Should().Equal(
            new RagasRetrievedContext("Json context", "chunk-json", "docs/json.md", "ref-json"));
    }

    [Fact]
    public void ExtractContexts_WhenRawDataIsNull_ReturnsEmpty()
    {
        var contexts = LightRagRagasQueryClient.ExtractContexts(null);

        contexts.Should().BeEmpty();
    }

    [Fact]
    public void ExtractContexts_WhenChunksAreMissingOrMalformed_ReturnsEmpty()
    {
        var missingChunks = new Dictionary<string, object>
        {
            ["data"] = new Dictionary<string, object>()
        };
        var malformedChunks = new Dictionary<string, object>
        {
            ["data"] = new Dictionary<string, object>
            {
                ["chunks"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["content"] = " "
                    },
                    "not-a-chunk"
                }
            }
        };

        LightRagRagasQueryClient.ExtractContexts(missingChunks).Should().BeEmpty();
        LightRagRagasQueryClient.ExtractContexts(malformedChunks).Should().BeEmpty();
    }
}
