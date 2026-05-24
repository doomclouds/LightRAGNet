namespace LightRAGNet.Services.QueryCache;

public sealed class CacheMetricsOptions
{
    public bool Enabled { get; set; } = true;

    public int RetentionDays { get; set; } = 30;

    public int MaxEvents { get; set; } = 20000;
}
