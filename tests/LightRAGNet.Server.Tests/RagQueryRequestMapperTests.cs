using FluentAssertions;
using LightRAGNet.Core.Models;
using LightRAGNet.Server.Services;
using LightRAGNet.Share.Models;

namespace LightRAGNet.Server.Tests;

public sealed class RagQueryRequestMapperTests
{
    [Fact]
    public void ToQueryParam_MapsRequestOptions()
    {
        var request = new RagQueryRequest
        {
            Query = "hello",
            Mode = QueryMode.Naive,
            Stream = false,
            IncludeReferences = false,
            ResponseType = "Bullet Points",
            TopK = 12,
            ChunkTopK = 6,
            EnableRerank = false,
            HighLevelKeywords = ["system"],
            LowLevelKeywords = ["queue"],
            OnlyNeedContext = true,
            OnlyNeedPrompt = false
        };

        var queryParam = RagQueryRequestMapper.ToQueryParam(request);

        queryParam.Mode.Should().Be(QueryMode.Naive);
        queryParam.Stream.Should().BeFalse();
        queryParam.IncludeReferences.Should().BeFalse();
        queryParam.ResponseType.Should().Be("Bullet Points");
        queryParam.TopK.Should().Be(12);
        queryParam.ChunkTopK.Should().Be(6);
        queryParam.EnableRerank.Should().BeFalse();
        queryParam.HighLevelKeywords.Should().Equal("system");
        queryParam.LowLevelKeywords.Should().Equal("queue");
        queryParam.OnlyNeedContext.Should().BeTrue();
        queryParam.OnlyNeedPrompt.Should().BeFalse();
        queryParam.ConversationHistory.Should().NotBeNull();
    }

    [Fact]
    public void ToMetadataEvent_UsesRequestAndQueryResult()
    {
        var request = new RagQueryRequest
        {
            Mode = QueryMode.Mix,
            Stream = false,
            IncludeReferences = true,
            ResponseType = "Multiple Paragraphs",
            HighLevelKeywords = ["architecture"],
            LowLevelKeywords = ["cache"]
        };

        var result = new QueryResult
        {
            Content = "answer",
            IsStreaming = false,
            RawData = new Dictionary<string, object>
            {
                ["data"] = new Dictionary<string, object>
                {
                    ["references"] = new List<Dictionary<string, object>>
                    {
                        new()
                        {
                            ["reference_id"] = "doc-1",
                            ["file_path"] = "guide.md"
                        }
                    }
                },
                ["metadata"] = new Dictionary<string, object>
                {
                    ["elapsed_ms"] = 42,
                    ["cache_status"] = "live"
                }
            }
        };

        var metadata = RagQueryRequestMapper.ToMetadataEvent(request, result);

        metadata.Mode.Should().Be(QueryMode.Mix);
        metadata.Stream.Should().BeFalse();
        metadata.IncludeReferences.Should().BeTrue();
        metadata.CachePolicy.Should().Be("Cacheable request");
        metadata.References.Should().ContainSingle();
        metadata.References[0].ReferenceId.Should().Be("doc-1");
        metadata.References[0].FilePath.Should().Be("guide.md");
        metadata.HighLevelKeywords.Should().Equal("architecture");
        metadata.LowLevelKeywords.Should().Equal("cache");
        metadata.Diagnostics.Should().ContainKey("elapsed_ms").WhoseValue.Should().Be("42");
        metadata.Diagnostics.Should().ContainKey("cache_status").WhoseValue.Should().Be("live");
    }

    [Fact]
    public void ToMetadataEvent_PrefersRuntimeKeywordsAndFormatsComplexDiagnostics()
    {
        var request = new RagQueryRequest
        {
            HighLevelKeywords = [],
            LowLevelKeywords = []
        };

        var result = new QueryResult
        {
            RawData = new Dictionary<string, object>
            {
                ["metadata"] = new Dictionary<string, object>
                {
                    ["high_level_keywords"] = new[] { "cache", "rag" },
                    ["low_level_keywords"] = new List<object> { "chunk", "graph" },
                    ["diagnostic_tags"] = new[] { "cache", "rag" },
                    ["processing_info"] = new Dictionary<string, object>
                    {
                        ["chunks"] = 2,
                        ["keywords"] = new Dictionary<string, object>
                        {
                            ["high_level"] = new[] { "采集流程", "简述" },
                            ["low_level"] = new[] { "100字" }
                        }
                    }
                }
            }
        };

        var metadata = RagQueryRequestMapper.ToMetadataEvent(request, result);

        metadata.HighLevelKeywords.Should().Equal("cache", "rag");
        metadata.LowLevelKeywords.Should().Equal("chunk", "graph");
        metadata.Diagnostics["diagnostic_tags"].Should().Contain("cache");
        metadata.Diagnostics["diagnostic_tags"].Should().Contain("rag");
        metadata.Diagnostics["diagnostic_tags"].Should().NotBe("System.String[]");
        metadata.Diagnostics["processing_info"].Should().Contain("\"chunks\":2");
        metadata.Diagnostics["processing_info"].Should().Contain("采集流程");
        metadata.Diagnostics["processing_info"].Should().Contain("100字");
        metadata.Diagnostics["processing_info"].Should().NotContain("\\u91C7");
    }
}
