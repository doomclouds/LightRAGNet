namespace LightRAGNet.Services.QueryCache;

public static class CacheReadOutcome
{
    public const string Hit = "hit";
    public const string Miss = "miss";
    public const string Invalid = "invalid";
    public const string Disabled = "disabled";
    public const string Error = "error";
}
