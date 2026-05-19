namespace LightRAGNet.Core.IO;

public sealed record AtomicFileWriteOptions(
    int MaxReplaceAttempts = 10,
    TimeSpan? RetryDelay = null)
{
    public TimeSpan EffectiveRetryDelay => RetryDelay ?? TimeSpan.FromMilliseconds(50);
}
