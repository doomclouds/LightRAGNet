using FluentAssertions;
using LightRAGNet.Core.Models;

namespace LightRAGNet.Tests.Query;

public sealed class QueryResultReferenceListTests
{
    [Fact]
    public void ReferenceList_WhenReferencesAreDictionaryList_ReturnsReferences()
    {
        var result = new QueryResult
        {
            RawData = new Dictionary<string, object>
            {
                ["data"] = new Dictionary<string, object>
                {
                    ["references"] = new List<Dictionary<string, object>>
                    {
                        new()
                        {
                            ["reference_id"] = "1",
                            ["file_path"] = "docs/a.md"
                        }
                    }
                }
            }
        };

        result.ReferenceList.Should().ContainSingle(reference =>
            reference.ReferenceId == "1" &&
            reference.FilePath == "docs/a.md");
    }

    [Fact]
    public void ReferenceList_WhenReferencesAreObjectList_ReturnsReferences()
    {
        var result = new QueryResult
        {
            RawData = new Dictionary<string, object>
            {
                ["data"] = new Dictionary<string, object>
                {
                    ["references"] = new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            ["reference_id"] = "2",
                            ["file_path"] = "docs/b.md"
                        }
                    }
                }
            }
        };

        result.ReferenceList.Should().ContainSingle(reference =>
            reference.ReferenceId == "2" &&
            reference.FilePath == "docs/b.md");
    }

    [Theory]
    [MemberData(nameof(MissingReferencesRawData))]
    public void ReferenceList_WhenRawDataDoesNotContainReferences_ReturnsEmpty(Dictionary<string, object>? rawData)
    {
        var result = new QueryResult { RawData = rawData };

        result.ReferenceList.Should().BeEmpty();
    }

    public static TheoryData<Dictionary<string, object>?> MissingReferencesRawData()
    {
        return new TheoryData<Dictionary<string, object>?>
        {
            null,
            new(),
            new()
            {
                ["data"] = new Dictionary<string, object>()
            }
        };
    }
}
