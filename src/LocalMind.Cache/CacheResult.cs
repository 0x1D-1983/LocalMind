namespace LocalMind.Cache;

public readonly record struct CacheResult<T>(
    T Value,
    bool IsHit,
    DateTimeOffset CachedAt = default)
{
    public static CacheResult<T> Miss() => new(default!, false);
    public static CacheResult<T> Hit(T value, DateTimeOffset cachedAt) => new(value, true, cachedAt);
}