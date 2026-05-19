using FluentAssertions;
using LightRAGNet.Core.IO;

namespace LightRAGNet.Tests.Utilities;

public sealed class AtomicFileWriterTests
{
    [Fact]
    public async Task WriteAllTextAsync_WhenTargetMissing_CreatesFileAndRemovesTempFile()
    {
        using var tempDirectory = new TempDirectory();
        var targetPath = Path.Combine(tempDirectory.Path, "nested", "state.json");

        await AtomicFileWriter.WriteAllTextAsync(
            targetPath,
            "{\"state\":\"ready\"}",
            new AtomicFileWriteOptions(RetryDelay: TimeSpan.Zero));

        File.ReadAllText(targetPath).Should().Be("{\"state\":\"ready\"}");
        Directory.GetFiles(tempDirectory.Path, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAllTextAsync_WhenCancellationRequestedBeforeReplace_ThrowsAndCleansTempFile()
    {
        using var tempDirectory = new TempDirectory();
        var targetPath = Path.Combine(tempDirectory.Path, "state.json");
        using var cancellationTokenSource = new CancellationTokenSource();
        var attempts = 0;
        var tempWasWritten = false;

        var act = async () => await AtomicFileWriter.WriteAllTextAsync(
            targetPath,
            "cancelled",
            new AtomicFileWriteOptions(MaxReplaceAttempts: 2, RetryDelay: TimeSpan.FromMilliseconds(1)),
            cancellationTokenSource.Token,
            replaceFile: (source, _) =>
            {
                attempts++;
                File.ReadAllText(source).Should().Be("cancelled");
                tempWasWritten = true;
                cancellationTokenSource.Cancel();
                throw new IOException("temporary lock");
            });

        await act.Should().ThrowAsync<OperationCanceledException>();
        attempts.Should().Be(1);
        tempWasWritten.Should().BeTrue();
        File.Exists(targetPath).Should().BeFalse();
        Directory.GetFiles(tempDirectory.Path, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAllTextAsync_WhenReplaceFailsTemporarily_RetriesThenSucceeds()
    {
        using var tempDirectory = new TempDirectory();
        var targetPath = Path.Combine(tempDirectory.Path, "state.json");
        var attempts = 0;

        await AtomicFileWriter.WriteAllTextAsync(
            targetPath,
            "updated",
            new AtomicFileWriteOptions(MaxReplaceAttempts: 2, RetryDelay: TimeSpan.Zero),
            replaceFile: (source, destination) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new IOException("temporary lock");
                }

                File.Move(source, destination, overwrite: true);
            });

        attempts.Should().Be(2);
        File.ReadAllText(targetPath).Should().Be("updated");
        Directory.GetFiles(tempDirectory.Path, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAllTextAsync_WhenReplaceKeepsFailing_ThrowsAndCleansTempFile()
    {
        using var tempDirectory = new TempDirectory();
        var targetPath = Path.Combine(tempDirectory.Path, "state.json");
        var attempts = 0;

        var act = async () => await AtomicFileWriter.WriteAllTextAsync(
            targetPath,
            "updated",
            new AtomicFileWriteOptions(MaxReplaceAttempts: 3, RetryDelay: TimeSpan.Zero),
            replaceFile: (_, _) =>
            {
                attempts++;
                throw new IOException("temporary lock");
            });

        await act.Should().ThrowAsync<IOException>().WithMessage("temporary lock");
        attempts.Should().Be(3);
        File.Exists(targetPath).Should().BeFalse();
        Directory.GetFiles(tempDirectory.Path, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"LightRAGNet-{Guid.NewGuid():N}");
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
