namespace FileSearchWebServer;

public class SearchCache
{
    // CacheEntry predstavlja jedan unos u cache-u.
    private class CacheEntry
    {
        public string Key { get; }
        public List<string>? Result { get; set; }
        public bool IsReady { get; set; }
        public object Sync { get; } = new object();

        public CacheEntry(string key)
        {
            Key = key;
        }
    }

    private readonly int _maxSize;
    private readonly Dictionary<string, CacheEntry> _entries = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _lru = new LinkedList<string>();
    private readonly object _cacheLock = new object();
    private readonly ThreadSafeLogger _logger;

    public SearchCache(int maxSize, ThreadSafeLogger logger)
    {
        _maxSize = maxSize;
        _logger = logger;
    }


    public List<string> GetOrCreate(string keyword, Func<List<string>> factory)
    {
        CacheEntry entry;

        // Promenljiva koja oznacava da li trenutna nit treba da racuna rezultat.
        bool shouldCompute = false;

        lock (_cacheLock)
        {

            if (_entries.TryGetValue(keyword, out entry!))
            {
                MoveToFront(keyword);

                _logger.Log($"CACHE HIT: {keyword}");
            }
            else
            {
                
                // entry se ubacuje u dictionary PRE nego sto se rezultat izracuna.
                entry = new CacheEntry(keyword);

                _entries[keyword] = entry;
                _lru.AddFirst(keyword);

                // Ako je cache prevelik
                TrimIfNeeded();

                shouldCompute = true;

                _logger.Log($"CACHE MISS: {keyword}");
            }
        }

        if (shouldCompute)
        {
            List<string> result;

            try
            {
                result = factory();
            }
            catch
            {
                
                lock (_cacheLock)
                {
                    _entries.Remove(keyword);
                    _lru.Remove(keyword);
                }

                lock (entry.Sync)
                {
                    entry.IsReady = true;
                    Monitor.PulseAll(entry.Sync);
                }

                throw;
            }


            lock (entry.Sync)
            {
                entry.Result = result;
                entry.IsReady = true;

                // Budimo sve niti koje su cekale isti keyword.
                Monitor.PulseAll(entry.Sync);
            }

            return result;
        }

        lock (entry.Sync)
        {

            // ova nit ceka da nit koja racuna rezultat zavrsi
            while (!entry.IsReady)
            {
                _logger.Log($"CACHE STAMPEDE WAIT: {keyword}");

                Monitor.Wait(entry.Sync);
            }

            return new List<string>(entry.Result ?? new List<string>());
        }
    }

    // Ova metoda se poziva samo dok je _cacheLock vec zakljucan.
    private void MoveToFront(string key)
    {
        _lru.Remove(key);
        _lru.AddFirst(key);
    }

    // Brise najstarije unose ako cache predje maksimalnu velicinu.
    // Ova metoda se poziva samo dok je _cacheLock vec zakljucan.
    private void TrimIfNeeded()
    {
        while (_entries.Count > _maxSize && _lru.Last != null)
        {
            // Poslednji element LinkedList-e je najmanje skoro koriscen keyword.
            string keyToRemove = _lru.Last.Value;

            _lru.RemoveLast();
            _entries.Remove(keyToRemove);

            _logger.Log($"CACHE REMOVE LRU: {keyToRemove}");
        }
    }
}
