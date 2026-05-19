using System.Text;

namespace LightRAGNet.Core.IO;

public static class AtomicFileWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task WriteAllTextAsync(
        string targetPath,
        string content,
        AtomicFileWriteOptions? options = null,
        CancellationToken cancellationToken = default,
        Action<string, string>? replaceFile = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(content);

        options ??= new AtomicFileWriteOptions();
        if (options.MaxReplaceAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaxReplaceAttempts,
                "MaxReplaceAttempts must be greater than zero.");
        }

        var parentDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        var tempPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";
        replaceFile ??= static (source, destination) => File.Move(source, destination, overwrite: true);

        try
        {
            await File.WriteAllTextAsync(tempPath, content, Utf8NoBom, cancellationToken);

            for (var attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    replaceFile(tempPath, targetPath);
                    return;
                }
                catch (Exception exception) when (IsRetryableReplaceFailure(exception)
                                                 && attempt < options.MaxReplaceAttempts)
                {
                    await Task.Delay(options.EffectiveRetryDelay, cancellationToken);
                }
            }
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }

    private static bool IsRetryableReplaceFailure(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException;
    }

    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
