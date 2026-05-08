using FileSearchWebServer.Models;

namespace FileSearchWebServer.Services;

public class CacheService
{
    private readonly Dictionary<string, CacheItem> _cache = new();
    private readonly Dictionary<string, object> _locks = new();

    private readonly object _globalLock = new();

    private const int MAX_CACHE_SIZE = 5;

    public bool TryGet(string key, out string value)
    {
        lock (_globalLock)
        {
            if (_cache.ContainsKey(key))
            {
                value = _cache[key].HtmlResponse;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }
    public object GetLock(string key)
    {
        lock (_globalLock)
        {
            if (!_locks.ContainsKey(key))
                _locks[key] = new object();

            return _locks[key];
        }
    }

    public void Set(string key, string value)
    {
        lock (_globalLock)
        {
            if (_cache.Count >= MAX_CACHE_SIZE)
            {
                var oldest = _cache
                    .OrderBy(x => x.Value.CreatedAt)
                    .First();

                _cache.Remove(oldest.Key);
            }
             _cache[key] = new CacheItem
            {
                Key = key,
                HtmlResponse = value,
                CreatedAt = DateTime.Now
            };
        }
    }
}