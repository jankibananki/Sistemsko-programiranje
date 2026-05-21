using System.Net;
using System.Text;

namespace FileSearchWebServer;

public class WebServer
{
    private readonly string _url;
    private readonly int _port;
    private readonly int _workerCount;

    // Deljeni red zahteva.
    //
    // Accept nit upisuje u ovaj red.
    // Worker niti citaju iz ovog reda.
    private readonly BlockingRequestQueue _requestQueue = new BlockingRequestQueue();
    private readonly ThreadSafeLogger _logger;
    private readonly SearchCache _cache;
    private readonly FileSearchService _fileService;

    // volatile je bitan jer ovu promenljivu cita vise niti.
    // Kada jedna nit promeni vrednost, druge niti treba da vide promenu.
    private volatile bool _running = true;
    private HttpListener? _listener;

    public WebServer(string url, int port, string rootDir, string logPath, int workerCount, int cacheSize)
    {
        _url = url;
        _port = port;
        _workerCount = workerCount;

        _logger = new ThreadSafeLogger(logPath);
        _cache = new SearchCache(cacheSize, _logger);
        _fileService = new FileSearchService(rootDir);
    }

    public void Run()
    {
        
        StartWorkers();

        string prefix = $"{_url}:{_port}/";
        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);
        _listener.Start();

        _logger.Log("Server pokrenut: " + prefix);
        _logger.Log("Broj worker niti: " + _workerCount);

        //GLAVNA ACCEPT/LISTENING PETLJA.
        while (_running)
        {
            try
            {
                HttpListenerContext context = _listener.GetContext();
                _requestQueue.Enqueue(new ClientRequest(context));
                _logger.Log("Zahtev dodat u red. Trenutna velicina reda: " + _requestQueue.Count);
            }
            catch (HttpListenerException)
            {
                if (!_running)
                {
                    break;
                }

                throw;
            }
            catch (ObjectDisposedException)
            {
                if (!_running)
                {
                    break;
                }

                throw;
            }
        }

        _logger.Log("Accept petlja je zavrsena.");
    }

    private void StartWorkers()
    {
        for (int i = 0; i < _workerCount; i++)
        {
            int workerId = i + 1;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.CurrentThread.Name = "Worker-" + workerId;
                _logger.Log($"ThreadPool worker pokrenut: Worker-{workerId}");

                WorkerLoop();
            });
        }
    }

    private void WorkerLoop()
    {
        while (_running)
        {
            ClientRequest? request = _requestQueue.Dequeue();

            if (request == null)
            {
                break;
            }

            try
            {
                ProcessRequest(request.Context);
            }
            catch (Exception ex)
            {
                _logger.LogException(ex);

                try
                {
                    SendHtml(
                        request.Context.Response,
                        "Greska",
                        "Doslo je do greske na serveru.",
                        HttpStatusCode.InternalServerError
                    );
                }
                catch
                {
                    // Ako je klijent u medjuvremenu zatvorio konekciju,
                    // slanje odgovora moze da pukne.
                    // To nije razlog da server padne.
                }
            }
        }

        _logger.Log("Worker nit je zavrsila rad.");
    }

    // ProcessRequest obradjuje jedan HTTP zahtev.
    private void ProcessRequest(HttpListenerContext context)
    {
        
        if (!context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            SendHtml(
                context.Response,
                "Greska",
                "Server podrzava samo GET metodu.",
                HttpStatusCode.MethodNotAllowed
            );
            return;
        }

        
        string rawPath = context.Request.Url?.AbsolutePath ?? "/";
        string value = Uri.UnescapeDataString(rawPath.Trim('/'));

        if (string.IsNullOrWhiteSpace(value))
        {
            SendHomePage(context.Response);
            return;
        }

        value = Path.GetFileName(value);
        
        _logger.Log("Obrada zahteva: " + value);

        if (_fileService.FileExistsInRoot(value))
        {
            SendFile(context.Response, value);
            return;
        }

        SendSearchResult(context.Response, value);
    }

    private void SendSearchResult(HttpListenerResponse response, string keyword)
    {
        
        List<string> files = _cache.GetOrCreate(keyword, () => _fileService.SearchByKeyword(keyword));

        StringBuilder body = new StringBuilder();

        body.Append($"<p>Rezultati pretrage za kljucnu rec: <b>{Html(keyword)}</b></p>");

        if (files.Count == 0)
        {
            body.Append("<p>Nijedan fajl ne zadovoljava kriterijum pretrage.</p>");
        }
        else
        {
            body.Append("<ul>");

            foreach (string file in files)
            {
                string href = $"http://localhost:{_port}/{Uri.EscapeDataString(file)}";
                body.Append($"<li><a href=\"{href}\">{Html(file)}</a></li>");
            }

            body.Append("</ul>");
        }

        SendHtml(response, "Rezultati pretrage", body.ToString(), HttpStatusCode.OK);
    }

    private void SendFile(HttpListenerResponse response, string fileName)
    {
        
        byte[] data = _fileService.ReadFile(fileName);
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "application/octet-stream";
        // Content-Disposition: attachment govori browseru da ponudi download.
        response.AddHeader("Content-Disposition", $"attachment; filename=\"{fileName}\"");
        response.ContentLength64 = data.Length;
        response.OutputStream.Write(data, 0, data.Length);
        response.OutputStream.Close();

        _logger.Log("Download fajla: " + fileName);
    }

    private void SendHomePage(HttpListenerResponse response)
    {
        string body = $@"
            <p>Unesi kljucnu rec u URL, npr:</p>
            <p><a href='http://localhost:{_port}/dotnet'>http://localhost:{_port}/dotnet</a></p>
            <form method='get' onsubmit='location.href=""/"" + encodeURIComponent(document.getElementById(""keyword"").value); return false;'>
                <input id='keyword' placeholder='kljucna rec' />
                <button type='submit'>Pretrazi</button>
            </form>";

        SendHtml(response, "File Search Web Server", body, HttpStatusCode.OK);
    }

    
    private void SendHtml(HttpListenerResponse response, string title, string body, HttpStatusCode statusCode)
    {
        
        string html = $@"<!DOCTYPE html>
        <html>
        <head>
            <meta charset='utf-8'>
            <title>{Html(title)}</title>
            <style>
                body {{ font-family: Arial, sans-serif; margin: 40px; background: #f5f5f5; }}
                main {{ background: white; padding: 25px; border-radius: 10px; max-width: 800px; }}
                a {{ color: #7a0019; font-weight: bold; }}
                input, button {{ padding: 8px; }}
            </style>
        </head>
        <body>
            <main>
                <h1>{Html(title)}</h1>
                {body}
            </main>
        </body>
        </html>";

        byte[] buffer = Encoding.UTF8.GetBytes(html);

        response.StatusCode = (int)statusCode;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = buffer.Length;

        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.OutputStream.Close();
    }

    // HTML encoding.
    private static string Html(string text)
    {
        return WebUtility.HtmlEncode(text);
    }

    // Stop metoda je centralni deo graceful shutdown-a.
    public void Stop()
    {
        _running = false;

        _logger.Log("Graceful shutdown pokrenut.");

        try
        {
            _listener?.Stop();
        }
        catch
        {
            
        }
        _requestQueue.Stop();
    }
}
