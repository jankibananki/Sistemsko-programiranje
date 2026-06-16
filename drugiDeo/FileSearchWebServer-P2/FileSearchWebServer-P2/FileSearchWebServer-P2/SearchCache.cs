namespace FileSearchWebServer;

public class SearchCache
{
    private sealed class CacheEntry
    {
        public CacheEntry(string key)
        {
            Key = key;
        }

        public string Key { get; }
        public DateTime LastAccessUtc { get; set; } = DateTime.UtcNow;
        public TaskCompletionSource<List<string>> Completion { get; } =
            new TaskCompletionSource<List<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly int _maxSize;
    private readonly TimeSpan _entryExpiration;
    private readonly Dictionary<string, CacheEntry> _entries =
        new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _lru = new LinkedList<string>();
    private readonly ReaderWriterLockSlim _cacheLock = new ReaderWriterLockSlim();
    private readonly ThreadSafeLogger _logger;

    public SearchCache(int maxSize, TimeSpan entryExpiration, ThreadSafeLogger logger)
    {
        if (maxSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSize), "Cache size mora biti veci od nule.");

        if (entryExpiration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(entryExpiration), "Expiration mora biti veci od nule.");

        _maxSize = maxSize;
        _entryExpiration = entryExpiration;
        _logger = logger;
    }

    public async Task<List<string>> GetOrCreateAsync(
        string keyword,
        Func<CancellationToken, Task<List<string>>> factory,
        CancellationToken cancellationToken)
    {
        CacheEntry entry;
        bool shouldCompute = false;

        _cacheLock.EnterUpgradeableReadLock();
        try
        {
            DateTime now = DateTime.UtcNow;

            if (_entries.TryGetValue(keyword, out entry!))
            {
                _cacheLock.EnterWriteLock();
                try
                {
                    RemoveExpiredReadyEntries(now);

                    if (!_entries.TryGetValue(keyword, out entry!))
                    {
                        entry = new CacheEntry(keyword);
                        _entries[keyword] = entry;
                        _lru.AddFirst(keyword);
                        shouldCompute = true;
                        _logger.Log($"CACHE EXPIRED MISS: {keyword}");
                    }
                    else
                    {
                        entry.LastAccessUtc = now;
                        MoveToFront(keyword);
                        _logger.Log($"CACHE HIT: {keyword}");
                    }
                }
                finally
                {
                    _cacheLock.ExitWriteLock();
                }
            }
            else
            {
                _cacheLock.EnterWriteLock();
                try
                {
                    RemoveExpiredReadyEntries(now);
                    entry = new CacheEntry(keyword);
                    _entries[keyword] = entry;
                    _lru.AddFirst(keyword);
                    TrimReadyEntriesIfNeeded();
                    shouldCompute = true;
                    _logger.Log($"CACHE MISS: {keyword}");
                }
                finally
                {
                    _cacheLock.ExitWriteLock();
                }
            }
        }
        finally
        {
            _cacheLock.ExitUpgradeableReadLock();
        }

        if (shouldCompute)
        {
            Task<List<string>> factoryTask;
            try
            {
                factoryTask = factory(cancellationToken);
            }
            catch (Exception ex)
            {
                RemoveEntry(keyword);
                entry.Completion.TrySetException(ex);
                throw;
            }

            Task continuation = factoryTask.ContinueWith(t =>
            {
                if (t.IsCanceled)
                {
                    RemoveEntry(keyword);
                    entry.Completion.TrySetCanceled(cancellationToken);
                    _logger.Log($"CACHE CANCEL: {keyword}");
                    return;
                }

                if (t.IsFaulted)
                {
                    RemoveEntry(keyword);
                    entry.Completion.TrySetException(t.Exception!.GetBaseException());
                    _logger.Log($"CACHE ERROR: {keyword} - {t.Exception.GetBaseException().Message}");
                    return;
                }

                List<string> result = t.Result;
                entry.LastAccessUtc = DateTime.UtcNow;
                entry.Completion.TrySetResult(result);
                _logger.Log($"CACHE STORE: {keyword} ({result.Count} fajlova)");
                TrimAfterStore();
            }, TaskScheduler.Default);

            await continuation;
        }
        else if (!entry.Completion.Task.IsCompleted)
        {
            _logger.Log($"CACHE STAMPEDE WAIT: {keyword}");
        }

        List<string> cachedResult = await entry.Completion.Task.WaitAsync(cancellationToken);
        return new List<string>(cachedResult);
    }

    private void RemoveEntry(string key)
    {
        _cacheLock.EnterWriteLock();
        try
        {
            _entries.Remove(key);
            _lru.Remove(key);
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }
    }

    private void MoveToFront(string key)
    {
        _lru.Remove(key);
        _lru.AddFirst(key);
    }

    private void RemoveExpiredReadyEntries(DateTime now)
    {
        while (_lru.Last != null)
        {
            string keyToRemove = _lru.Last.Value;
            CacheEntry entry = _entries[keyToRemove];

            if (!entry.Completion.Task.IsCompleted || now - entry.LastAccessUtc < _entryExpiration)
                break;

            _lru.RemoveLast();
            _entries.Remove(keyToRemove);
            _logger.Log($"CACHE EXPIRE: {keyToRemove}");
        }
    }

    private void TrimReadyEntriesIfNeeded()
    {
        RemoveExpiredReadyEntries(DateTime.UtcNow);

        while (_entries.Count > _maxSize && _lru.Last != null)
        {
            string keyToRemove = _lru.Last.Value;
            CacheEntry entry = _entries[keyToRemove];

            if (!entry.Completion.Task.IsCompleted)
                break;

            _lru.RemoveLast();
            _entries.Remove(keyToRemove);
            _logger.Log($"CACHE REMOVE LRU: {keyToRemove}");
        }
    }

    private void TrimAfterStore()
    {
        _cacheLock.EnterWriteLock();
        try
        {
            TrimReadyEntriesIfNeeded();
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }
    }
}
