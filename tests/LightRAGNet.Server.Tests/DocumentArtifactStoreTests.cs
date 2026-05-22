using System.Text;
using FluentAssertions;
using LightRAGNet.Server.Models;
using LightRAGNet.Server.Services.DocumentArtifacts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Tests;

public sealed class DocumentArtifactStoreTests
{
    [Fact]
    public async Task SaveOriginalAsync_WritesOriginalFileUnderDocumentDirectory()
    {
        using var tempRoot = new TemporaryDirectory();
        var store = CreateStore(tempRoot.Path);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("pdf bytes"));

        var result = await store.SaveOriginalAsync(42, stream, "合同.pdf", CancellationToken.None);

        result.RelativePath.Should().Be(Path.Combine("documents", "42", "original.pdf"));
        result.AbsolutePath.Should().StartWith(tempRoot.Path);
        File.Exists(result.AbsolutePath).Should().BeTrue();
        result.Hash.Should().Be("d1cb546b102fab8362de413fdacc187b05be10df72b72db3b3e50b4953f6a555");
        result.Size.Should().Be(9);
    }

    [Fact]
    public async Task SaveConvertedMarkdownAsync_WritesConvertedMarkdownAndReadsItBack()
    {
        using var tempRoot = new TemporaryDirectory();
        var store = CreateStore(tempRoot.Path);
        const string markdown = "# Title\n\nBody";

        var result = await store.SaveConvertedMarkdownAsync(7, markdown, CancellationToken.None);
        var savedMarkdown = await store.ReadConvertedMarkdownAsync(result.RelativePath, CancellationToken.None);

        result.RelativePath.Should().Be(Path.Combine("documents", "7", "converted.md"));
        savedMarkdown.Should().Be(markdown);
        result.Hash.Should().Be("b7b510d34e84878ec3d4d2bdc287f223faef14f09c59bd3b51597c88a3d260c7");
    }

    [Fact]
    public async Task SaveConvertedMarkdownAsync_WritesUtf8WithoutBom()
    {
        using var tempRoot = new TemporaryDirectory();
        var store = CreateStore(tempRoot.Path);

        var result = await store.SaveConvertedMarkdownAsync(8, "中文 markdown", CancellationToken.None);
        var bytes = await File.ReadAllBytesAsync(result.AbsolutePath, CancellationToken.None);

        var startsWithUtf8Bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        startsWithUtf8Bom.Should().BeFalse();
    }

    [Fact]
    public async Task SaveOriginalAsync_AcceptsDocxAndWritesOriginalDocx()
    {
        using var tempRoot = new TemporaryDirectory();
        var store = CreateStore(tempRoot.Path);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("docx bytes"));

        var result = await store.SaveOriginalAsync(43, stream, "sample.docx", CancellationToken.None);

        result.RelativePath.Should().Be(Path.Combine("documents", "43", "original.docx"));
        File.Exists(result.AbsolutePath).Should().BeTrue();
    }

    [Fact]
    public void GetFileInfo_WhenPathEscapesRoot_Throws()
    {
        using var tempRoot = new TemporaryDirectory();
        var store = CreateStore(tempRoot.Path);
        var escapedPath = Path.Combine("documents", "..", "..", "secret.pdf");

        var act = () => store.GetFileInfo(escapedPath);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Document artifact path is outside the configured root.");
    }

    [Fact]
    public void Exists_WhenPathIsNullWhitespaceOrTraversal_ReturnsFalse()
    {
        using var tempRoot = new TemporaryDirectory();
        var store = CreateStore(tempRoot.Path);
        var escapedPath = Path.Combine("documents", "..", "..", "secret.pdf");

        store.Exists(null).Should().BeFalse();
        store.Exists("").Should().BeFalse();
        store.Exists("   ").Should().BeFalse();
        store.Exists(escapedPath).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteArtifactsAsync_RemovesDocumentDirectory()
    {
        using var tempRoot = new TemporaryDirectory();
        var store = CreateStore(tempRoot.Path);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("pdf bytes"));
        await store.SaveOriginalAsync(99, stream, "合同.pdf", CancellationToken.None);
        await store.SaveConvertedMarkdownAsync(99, "# Title", CancellationToken.None);

        await store.DeleteArtifactsAsync(new MarkdownDocument { Id = 99 }, CancellationToken.None);

        Directory.Exists(Path.Combine(tempRoot.Path, "documents", "99")).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteArtifactsAsync_RemovesOnlyTargetDocumentDirectory()
    {
        using var tempRoot = new TemporaryDirectory();
        var store = CreateStore(tempRoot.Path);
        await using var targetStream = new MemoryStream(Encoding.UTF8.GetBytes("pdf bytes"));
        await using var siblingStream = new MemoryStream(Encoding.UTF8.GetBytes("sibling"));
        await store.SaveOriginalAsync(100, targetStream, "target.pdf", CancellationToken.None);
        var siblingResult = await store.SaveOriginalAsync(101, siblingStream, "sibling.pdf", CancellationToken.None);

        await store.DeleteArtifactsAsync(new MarkdownDocument { Id = 100 }, CancellationToken.None);

        Directory.Exists(Path.Combine(tempRoot.Path, "documents", "100")).Should().BeFalse();
        Directory.Exists(Path.Combine(tempRoot.Path, "documents", "101")).Should().BeTrue();
        File.Exists(siblingResult.AbsolutePath).Should().BeTrue();
    }

    [Fact]
    public async Task SaveOriginalAsync_WhenFileTypeUnsupported_Throws()
    {
        using var tempRoot = new TemporaryDirectory();
        var store = CreateStore(tempRoot.Path);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("text bytes"));

        var act = () => store.SaveOriginalAsync(5, stream, "notes.txt", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static FileSystemDocumentArtifactStore CreateStore(string rootPath)
    {
        var options = Options.Create(new DocumentArtifactStoreOptions
        {
            RootPath = rootPath
        });

        return new FileSystemDocumentArtifactStore(
            options,
            NullLogger<FileSystemDocumentArtifactStore>.Instance);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lightragnet-artifacts-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
