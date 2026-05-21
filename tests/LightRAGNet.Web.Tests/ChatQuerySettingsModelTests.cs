using FluentAssertions;
using LightRAGNet.Core.Models;
using LightRAGNet.Share.Models;
using LightRAGNet.Web.Models;

namespace LightRAGNet.Web.Tests;

public sealed class ChatQuerySettingsModelTests
{
    [Fact]
    public void BuildRequest_BypassMode_DisablesReferences()
    {
        var settings = new ChatQuerySettingsModel
        {
            SelectedMode = QueryMode.Bypass,
            IncludeReferences = true
        };

        var request = settings.BuildRequest("hello");

        request.Mode.Should().Be(QueryMode.Bypass);
        request.IncludeReferences.Should().BeFalse();
    }

    [Theory]
    [InlineData(ChatQueryDebugOutputMode.Answer, false, false)]
    [InlineData(ChatQueryDebugOutputMode.ContextOnly, true, false)]
    [InlineData(ChatQueryDebugOutputMode.PromptOnly, false, true)]
    public void BuildRequest_DebugOutputMode_BuildsMutuallyExclusiveFlags(
        ChatQueryDebugOutputMode debugOutputMode,
        bool expectedOnlyNeedContext,
        bool expectedOnlyNeedPrompt)
    {
        var settings = new ChatQuerySettingsModel
        {
            DebugOutputMode = debugOutputMode
        };

        var request = settings.BuildRequest("hello");

        request.OnlyNeedContext.Should().Be(expectedOnlyNeedContext);
        request.OnlyNeedPrompt.Should().Be(expectedOnlyNeedPrompt);
    }

    [Fact]
    public void ParseKeywords_SplitsTrimsAndDistinctsCaseInsensitively()
    {
        var keywords = ChatQuerySettingsModel.ParseKeywords(" Graph, vector，GRAPH\r\nchunk\n vector ");

        keywords.Should().Equal("Graph", "vector", "chunk");
    }

    [Fact]
    public void ApplyMetadata_CopiesQueryMetadataToChatMessage()
    {
        var message = new ChatMessageModel
        {
            Role = "Assistant",
            Text = "pending",
            References = [new RagQueryReferenceDto { FilePath = "old.md", ReferenceId = "old" }],
            Diagnostics = new Dictionary<string, string> { ["old"] = "value" }
        };
        var metadata = new QueryMetadataEvent
        {
            Mode = QueryMode.Hybrid,
            Stream = false,
            IncludeReferences = true,
            References = [new RagQueryReferenceDto { FilePath = "doc.md", ReferenceId = "r1" }],
            HighLevelKeywords = ["Architecture"],
            LowLevelKeywords = ["chunk"],
            Diagnostics = new Dictionary<string, string> { ["cache"] = "hit" }
        };

        ChatQuerySettingsModel.ApplyMetadata(message, metadata);

        message.Mode.Should().Be(QueryMode.Hybrid);
        message.IsStreaming.Should().BeFalse();
        message.IsCacheable.Should().BeTrue();
        message.References.Should().ContainSingle()
            .Which.FilePath.Should().Be("doc.md");
        message.HighLevelKeywords.Should().Equal("Architecture");
        message.LowLevelKeywords.Should().Equal("chunk");
        message.Diagnostics.Should().ContainKey("cache").WhoseValue.Should().Be("hit");
    }

    [Fact]
    public void ApplyMetadata_ReferenceDisabled_ClearsReferences()
    {
        var message = new ChatMessageModel
        {
            References = [new RagQueryReferenceDto { FilePath = "old.md", ReferenceId = "old" }]
        };
        var metadata = new QueryMetadataEvent
        {
            IncludeReferences = false,
            References = [new RagQueryReferenceDto { FilePath = "doc.md", ReferenceId = "r1" }]
        };

        ChatQuerySettingsModel.ApplyMetadata(message, metadata);

        message.References.Should().BeEmpty();
    }

    [Fact]
    public void CloneRequest_CopiesListsSoHistoryDoesNotFollowToolbarMutation()
    {
        var request = new RagQueryRequest
        {
            Query = "inspect retrieval",
            Mode = QueryMode.Mix,
            Stream = true,
            IncludeReferences = true,
            ResponseType = "Concise",
            TopK = 5,
            ChunkTopK = 3,
            EnableRerank = true,
            HighLevelKeywords = ["graph"],
            LowLevelKeywords = ["chunk"],
            OnlyNeedContext = false,
            OnlyNeedPrompt = false
        };

        var clone = ChatQuerySettingsModel.CloneRequest(request);

        clone.Should().NotBeSameAs(request);
        clone.HighLevelKeywords.Should().NotBeSameAs(request.HighLevelKeywords);
        clone.LowLevelKeywords.Should().NotBeSameAs(request.LowLevelKeywords);
        clone.HighLevelKeywords.Should().Equal("graph");
        clone.LowLevelKeywords.Should().Equal("chunk");

        request.HighLevelKeywords.Add("mutated");
        request.LowLevelKeywords.Add("changed");

        clone.HighLevelKeywords.Should().Equal("graph");
        clone.LowLevelKeywords.Should().Equal("chunk");
    }
}
