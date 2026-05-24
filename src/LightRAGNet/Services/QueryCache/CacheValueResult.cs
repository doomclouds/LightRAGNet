namespace LightRAGNet.Services.QueryCache;

public sealed record CacheValueResult<T>(
    T Value,
    bool CacheEnabled,
    bool Hit,
    bool Saved,
    string? CacheKey,
    string CacheType,
    TimeSpan CacheLookupDuration,
    TimeSpan? FactoryDuration)
{
    public static CacheValueResult<T> FromHit(
        T value,
        string cacheType,
        string cacheKey,
        TimeSpan cacheLookupDuration)
    {
        return new CacheValueResult<T>(
            value,
            CacheEnabled: true,
            Hit: true,
            Saved: false,
            cacheKey,
            cacheType,
            cacheLookupDuration,
            FactoryDuration: null);
    }

    public static CacheValueResult<T> FromMiss(
        T value,
        bool cacheEnabled,
        bool saved,
        string? cacheKey,
        string cacheType,
        TimeSpan cacheLookupDuration,
        TimeSpan factoryDuration)
    {
        return new CacheValueResult<T>(
            value,
            cacheEnabled,
            Hit: false,
            saved,
            cacheKey,
            cacheType,
            cacheLookupDuration,
            factoryDuration);
    }
}
