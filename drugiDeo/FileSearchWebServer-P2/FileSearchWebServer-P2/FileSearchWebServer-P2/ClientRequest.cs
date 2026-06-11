using System.Net;

namespace FileSearchWebServer;

public class ClientRequest
{
    public HttpListenerContext Context { get; }

    public ClientRequest(HttpListenerContext context)
    {
        Context = context;
    }
}
