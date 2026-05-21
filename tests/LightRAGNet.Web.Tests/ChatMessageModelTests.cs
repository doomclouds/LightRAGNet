using FluentAssertions;
using LightRAGNet.Core.Models;
using LightRAGNet.Share.Models;
using LightRAGNet.Web.Models;

namespace LightRAGNet.Web.Tests;

public sealed class ChatMessageModelTests
{
    [Fact]
    public void ChatMessageModel_DefaultsMetadataCollections()
    {
        var message = new ChatMessageModel();

        message.Mode.Should().BeNull();
        message.IsStreaming.Should().BeFalse();
        message.IsCacheable.Should().BeFalse();
        message.IsComplete.Should().BeFalse();
        message.References.Should().BeEmpty();
        message.HighLevelKeywords.Should().BeEmpty();
        message.LowLevelKeywords.Should().BeEmpty();
        message.Diagnostics.Should().BeEmpty();
        message.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void ChatMessageModel_StoresQueryMetadata()
    {
        var message = new ChatMessageModel
        {
            Mode = QueryMode.Mix,
            IsStreaming = true,
            IsCacheable = true,
            IsComplete = true,
            ErrorMessage = "failed",
            References = [new RagQueryReferenceDto { ReferenceId = "r1", FilePath = "doc.md" }],
            HighLevelKeywords = ["architecture"],
            LowLevelKeywords = ["cache"],
            Diagnostics = new Dictionary<string, string> { ["source"] = "test" }
        };

        message.Mode.Should().Be(QueryMode.Mix);
        message.IsStreaming.Should().BeTrue();
        message.IsCacheable.Should().BeTrue();
        message.IsComplete.Should().BeTrue();
        message.ErrorMessage.Should().Be("failed");
        var reference = message.References.Should().ContainSingle().Subject;
        reference.ReferenceId.Should().Be("r1");
        reference.FilePath.Should().Be("doc.md");
        message.HighLevelKeywords.Should().Equal("architecture");
        message.LowLevelKeywords.Should().Equal("cache");
        message.Diagnostics.Should().ContainKey("source").WhoseValue.Should().Be("test");
    }

    [Fact]
    public void ChatMessageModel_DefaultsRetrievalDataState()
    {
        var message = new ChatMessageModel();

        message.RetrievalDataRequest.Should().BeNull();
        message.RetrievalData.Should().BeNull();
        message.IsLoadingRetrievalData.Should().BeFalse();
        message.RetrievalDataError.Should().BeNull();
    }
}
