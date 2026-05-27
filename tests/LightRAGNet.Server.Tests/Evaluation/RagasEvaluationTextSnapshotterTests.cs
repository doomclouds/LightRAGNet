using FluentAssertions;
using LightRAGNet.Server.Services.Evaluation;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Tests.Evaluation;

public sealed class RagasEvaluationTextSnapshotterTests
{
    [Fact]
    public void Snapshot_DefaultPolicy_StoresPreviewAndHashOnly()
    {
        var snapshotter = new RagasEvaluationTextSnapshotter(
            Options.Create(new RagasEvaluationOptions { PreviewMaxChars = 5 }));

        var snapshot = snapshotter.Snapshot("abcdef", includeFullText: false);

        snapshot.Preview.Should().Be("abcde");
        snapshot.Hash.Should().HaveLength(64);
        snapshot.Text.Should().BeNull();
    }

    [Fact]
    public void ValidateFullTextRequest_WhenConfigDisallowsFullText_ReturnsFailure()
    {
        var snapshotter = new RagasEvaluationTextSnapshotter(
            Options.Create(new RagasEvaluationOptions { AllowPersistFullText = false }));

        var result = snapshotter.ValidateFullTextRequest(includeFullText: true);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("full_text_disabled");
    }

    [Fact]
    public void Snapshot_WhenFullTextRequestedAndConfigAllowsFullText_StoresFullText()
    {
        var snapshotter = new RagasEvaluationTextSnapshotter(
            Options.Create(new RagasEvaluationOptions { AllowPersistFullText = true }));

        var snapshot = snapshotter.Snapshot("sensitive text", includeFullText: true);

        snapshot.Text.Should().Be("sensitive text");
    }

    [Fact]
    public void Snapshot_WhenFullTextNotRequested_OmitsFullTextEvenIfConfigAllowsFullText()
    {
        var snapshotter = new RagasEvaluationTextSnapshotter(
            Options.Create(new RagasEvaluationOptions { AllowPersistFullText = true }));

        var snapshot = snapshotter.Snapshot("sensitive text", includeFullText: false);

        snapshot.Text.Should().BeNull();
    }

    [Fact]
    public void Snapshot_Hash_IsLowercaseSha256HexAndStableForSameInput()
    {
        var snapshotter = new RagasEvaluationTextSnapshotter(
            Options.Create(new RagasEvaluationOptions()));

        var first = snapshotter.Snapshot("abcdef", includeFullText: false);
        var second = snapshotter.Snapshot("abcdef", includeFullText: false);

        first.Hash.Should().Be(second.Hash);
        first.Hash.Should().Be("bef57ec7f53a6d40beb640a780a639c83bc29ac8a9816f1fc6c5c6dcd93c4721");
        first.Hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Snapshot_WhenPreviewMaxCharsIsNotPositive_ProducesEmptyPreview(int previewMaxChars)
    {
        var snapshotter = new RagasEvaluationTextSnapshotter(
            Options.Create(new RagasEvaluationOptions { PreviewMaxChars = previewMaxChars }));

        var snapshot = snapshotter.Snapshot("abcdef", includeFullText: false);

        snapshot.Preview.Should().BeEmpty();
    }
}
