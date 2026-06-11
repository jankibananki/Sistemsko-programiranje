using System.Net;
using System.Text;

namespace FileSearchWebServer;

public class WebServer
{
    private readonly string _url;
    private readonly int _port;
    private readonly int _workerCount;
    private readonly BlockingRequestQueue _requestQueue = new BlockingRequestQueue();
    private readonly ThreadSafeLogger _logger;
    private readonly SearchCache _cache;
    private readonly FileSearchService _fileService;
    private readonly List<Task> _workerTasks = new List<Task>();

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

    public void StartWorkers()
    {
        for (int i = 0; i < _workerCount; i++)
        {
            int workerId = i + 1;
            Task workerTask = WorkerLoopAsync(workerId);
            _workerTasks.Add(workerTask);

            workerTask.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _logger.Log("Worker Task greska: " + t.Exception?.GetBaseException().Message);
                else
                    _logger.Log($"Worker {workerId} zavrsio rad.");
            }, TaskScheduler.Default);
        }

        _logger.Log($"Startovano worker Task-ova: {_workerCount}");
    }

    public void Run()
    {
        string prefix = $"{_url}:{_port}/";
        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);
        _listener.Start();

        _logger.Log("Server pokrenut: " + prefix);
        _logger.Log("Accept petlja prima zahteve, worker Task-ovi ih obradjuju iz reda.");

        while (_running)
        {
            try
            {
                HttpListenerContext context = _listener.GetContext();
                _requestQueue.Enqueue(new ClientRequest(context));
                _logger.Log($"Zahtev prihvacen i dodat u red. Duzina reda: {_requestQueue.Count}");
            }
            catch (HttpListenerException)
            {
                if (!_running)
                    break;

                throw;
            }
            catch (ObjectDisposedException)
            {
                if (!_running)
                    break;

                throw;
            }
        }

        WaitForWorkers();
        _logger.Log("Accept petlja i worker Task-ovi su zavrseni.");
    }

    private async Task WorkerLoopAsync(int workerId)
    {
        _logger.Log($"Worker {workerId} startovan.");

        while (true)
        {
            ClientRequest? request = await _requestQueue.DequeueAsync();
            if (request == null)
                break;

            try
            {
                await ProcessRequestAsync(request.Context);
            }
            catch (Exception ex)
            {
                _logger.LogException(ex);
                TrySendServerError(request.Context.Response);
            }
        }
    }

    private async Task ProcessRequestAsync(HttpListenerContext context)
    {
        if (!context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            SendHtml(context.Response, "Greska", "Server podrzava samo GET metodu.", HttpStatusCode.MethodNotAllowed);
            return;
        }

        string rawPath = context.Request.Url?.AbsolutePath ?? "/";
        string value = Uri.UnescapeDataString(rawPath.Trim('/'));

        if (value.Equals("favicon.ico", StringComparison.OrdinalIgnoreCase))
        {
            SendNoContent(context.Response);
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            SendHomePage(context.Response);
            return;
        }

        value = Path.GetFileName(value);
        _logger.Log("Obrada zahteva: " + value);

        if (_fileService.FileExistsInRoot(value))
        {
            await SendFileAsync(context.Response, value);
            return;
        }

        await SendSearchResultAsync(context.Response, value);
    }

    private async Task SendSearchResultAsync(HttpListenerResponse response, string keyword)
    {
        using CancellationTokenSource searchCts = new CancellationTokenSource();

        try
        {
            Task<List<string>> searchTask = _cache.GetOrCreateAsync(
                keyword,
                token => _fileService.SearchByKeywordAsync(keyword, token),
                searchCts.Token);
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(5), searchCts.Token);

            Task completedTask = await Task.WhenAny(searchTask, timeoutTask);
            if (completedTask == timeoutTask)
            {
                searchCts.Cancel();
                throw new OperationCanceledException(searchCts.Token);
            }

            searchCts.Cancel();
            List<string> files = await searchTask;

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
        catch (OperationCanceledException)
        {
            _logger.Log($"Pretraga otkazana zbog timeout-a: {keyword}");
            SendHtml(response, "Timeout", "Pretraga nije zavrsena u predvidjenom vremenu.", HttpStatusCode.RequestTimeout);
        }
    }

    private async Task SendFileAsync(HttpListenerResponse response, string fileName)
    {
        byte[] data = await _fileService.ReadFileAsync(fileName);

        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "application/octet-stream";
        response.AddHeader("Content-Disposition", $"attachment; filename=\"{fileName}\"");
        response.ContentLength64 = data.Length;

        await response.OutputStream.WriteAsync(data, 0, data.Length);
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
                main {{ background: white; padding: 25px; border-radius: 8px; max-width: 800px; }}
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

    private static void SendNoContent(HttpListenerResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.NoContent;
        response.ContentLength64 = 0;
        response.OutputStream.Close();
    }

    private void TrySendServerError(HttpListenerResponse response)
    {
        try
        {
            SendHtml(response, "Greska", "Doslo je do greske na serveru.", HttpStatusCode.InternalServerError);
        }
        catch
        {
            // Response je mozda vec zatvoren.
        }
    }

    private static string Html(string text) => WebUtility.HtmlEncode(text);

    public void Stop()
    {
        _running = false;
        _logger.Log("Graceful shutdown pokrenut.");

        try { _listener?.Stop(); }
        catch { }

        _requestQueue.Stop(_workerCount);
    }

    private void WaitForWorkers()
    {
        if (_workerTasks.Count == 0)
            return;

        _logger.Log($"Cekam da zavrse worker Task-ovi: {_workerTasks.Count}");
        Task.WaitAll(_workerTasks.ToArray());
    }
}
