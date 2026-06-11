namespace FileSearchWebServer;

public class BlockingRequestQueue
{
    private readonly Queue<ClientRequest> _queue = new Queue<ClientRequest>();
    private readonly SemaphoreSlim _availableItems = new SemaphoreSlim(0);
    private readonly object _lock = new object();
    private bool _acceptingRequests = true;

    public void Enqueue(ClientRequest request)
    {
        lock (_lock)
        {
            if (!_acceptingRequests)
                return;

            _queue.Enqueue(request);
        }

        _availableItems.Release();
    }

    public async Task<ClientRequest?> DequeueAsync()
    {
        while (true)
        {
            await _availableItems.WaitAsync();

            lock (_lock)
            {
                if (_queue.Count > 0)
                    return _queue.Dequeue();

                if (!_acceptingRequests)
                    return null;
            }
        }
    }

    public void Stop(int workerCount)
    {
        lock (_lock)
        {
            _acceptingRequests = false;
        }

        for (int i = 0; i < workerCount; i++)
            _availableItems.Release();
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _queue.Count;
            }
        }
    }
}
