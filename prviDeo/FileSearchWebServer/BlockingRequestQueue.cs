namespace FileSearchWebServer;

public class BlockingRequestQueue
{
    private readonly Queue<ClientRequest> _queue = new Queue<ClientRequest>();
    private readonly object _lock = new object();
    private bool _running = true;

    // Enqueue poziva accept/listening nit kada stigne novi HTTP zahtev.
    public void Enqueue(ClientRequest request)
    {
        lock (_lock)
        {
            // Ako je shutdown vec poceo, ne prihvatamo nove zahteve u red.
            if (!_running)
            {
                return;
            }

            _queue.Enqueue(request);

            Monitor.Pulse(_lock);
        }
    }

    public ClientRequest? Dequeue()
    {
        lock (_lock)
        {
            
            while (_queue.Count == 0 && _running)
            {
                Monitor.Wait(_lock);
            }

            // vracamo null da worker nit zna da treba da prekine rad
            if (_queue.Count == 0 && !_running)
            {
                return null;
            }

            return _queue.Dequeue();
        }
    }

    // Stop se poziva kada korisnik pritisne ESC
    public void Stop()
    {
        lock (_lock)
        {
            _running = false;
            
            Monitor.PulseAll(_lock);
        }
    }

    // Count vraca broj zahteva koji trenutno cekaju u redu.
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
