using Microsoft.AspNetCore.Mvc;
using FileSearchWebServer.Services;
using System.Text;

namespace FileSearchWebServer.Controllers;

[ApiController]
public class FileController : ControllerBase
{
    private readonly CacheService _cache;
    private readonly FileSearchService _search;
    private readonly LoggerService _logger;

    public FileController(
        CacheService cache,
        FileSearchService search,
        LoggerService logger)
    {
        _cache = cache;
        _search = search;
        _logger = logger;
    }

    [HttpGet("/")]
    public IActionResult Home()
    {
        string html = @"
        <!DOCTYPE html>
        <html>
        <head>
            <title>File Search Server</title>
            <style>
                *{
                    margin:0;
                    padding:0;
                    box-sizing:border-box;
                    font-family:Arial,sans-serif;
                }

                body{
                    background:#0f172a;
                    height:100vh;
                    display:flex;
                    justify-content:center;
                    align-items:center;
                    color:white;
                }

                .container{
                    width:600px;
                    background:#1e293b;
                    padding:40px;
                    border-radius:20px;
                    box-shadow:0 0 25px rgba(0,0,0,0.4);
                    text-align:center;
                }

                h1{
                    font-size:42px;
                    margin-bottom:15px;
                }

                p{
                    color:#94a3b8;
                    margin-bottom:30px;
                }

                form{
                    display:flex;
                    gap:10px;
                }

                input{
                    flex:1;
                    padding:15px;
                    border:none;
                    border-radius:12px;
                    background:#334155;
                    color:white;
                    font-size:16px;
                }

                input:focus{
                    outline:none;
                }

                button{
                    padding:15px 25px;
                    border:none;
                    border-radius:12px;
                    background:#3b82f6;
                    color:white;
                    cursor:pointer;
                    transition:0.3s;
                }

                button:hover{
                    background:#2563eb;
                }

                .footer{
                    margin-top:25px;
                    color:#64748b;
                    font-size:14px;
                }
            </style>
        </head>

        <body>
            <div class='container'>
                <h1>File Search Server</h1>

                <p>Pretraga fajlova po kljucnim recima</p>

                <form onsubmit='searchFiles(event)'>
                    <input
                        type='text'
                        id='keyword'
                        placeholder='Unesite kljucnu rec...'
                    />

                    <button type='submit'>
                        Search
                    </button>
                </form>

                <div class='footer'>
                    Sistemsko programiranje projekat
                </div>
            </div>

            <script>
                function searchFiles(event)
                {
                    event.preventDefault();

                    let keyword =
                        document.getElementById('keyword').value;

                    if(keyword.trim() === '')
                    {
                        return;
                    }

                    window.location.href =
                        '/' + keyword;
                }
            </script>
        </body>
        </html>
        ";

        return Content(html, "text/html");
    }

    [HttpGet("/{keyword}")]
    public IActionResult Search(string keyword)
    {
        if (keyword == "favicon.ico")
        {
            return NotFound();
        }

        if (keyword.Contains("."))
        {
            return Download(keyword);
        }

        if (_cache.TryGet(keyword, out string cached))
        {
            _logger.Log($"CACHE HIT: {keyword}");

            return Content(cached, "text/html");
        }

        lock (_cache.GetLock(keyword))
        {
            if (_cache.TryGet(keyword, out cached))
            {
                return Content(cached, "text/html");
            }

            _logger.Log($"CACHE MISS: {keyword}");

            var files = _search.Search(keyword);

            if (files.Count == 0)
            {
                return Content(
                    @"
                    <html>
                    <body style='background:#0f172a;color:white;font-family:Arial;text-align:center;padding-top:100px'>
                        <h1>Nema rezultata za pretragu</h1>
                        <a href='/' style='color:#3b82f6'>Nazad</a>
                    </body>
                    </html>
                    ",
                    "text/html"
                );
            }

            StringBuilder html = new();

            html.Append(@"
            <html>
            <head>
                <style>
                    body{
                        background:#0f172a;
                        color:white;
                        font-family:Arial;
                        padding:50px;
                    }

                    h1{
                        margin-bottom:30px;
                    }

                    .card{
                        background:#1e293b;
                        padding:20px;
                        border-radius:15px;
                        margin-bottom:15px;
                        display:flex;
                        justify-content:space-between;
                        align-items:center;
                    }

                    a{
                        color:white;
                        text-decoration:none;
                    }

                    .download{
                        background:#3b82f6;
                        padding:10px 15px;
                        border-radius:10px;
                    }

                    .back{
                        display:inline-block;
                        margin-top:25px;
                        color:#3b82f6;
                    }
                </style>
            </head>

            <body>
            <h1>Rezultati pretrage</h1>
            ");

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);

                html.Append($@"
                <div class='card'>
                    <span>{fileName}</span>

                    <a class='download'
                       href='/{fileName}'>
                        Download
                    </a>
                </div>
                ");
            }

            html.Append(@"
                <a class='back' href='/'>
                    Nazad
                </a>
            </body>
            </html>
            ");

            var result = html.ToString();

            _cache.Set(keyword, result);

            return Content(result, "text/html");
        }
    }

    private IActionResult Download(string fileName)
    {
        var path = _search.GetFile(fileName);

        if (path == null)
        {
            return NotFound("Fajl nije pronadjen");
        }

        var bytes = System.IO.File.ReadAllBytes(path);

        return File(
            bytes,
            "application/octet-stream",
            fileName
        );
    }
}