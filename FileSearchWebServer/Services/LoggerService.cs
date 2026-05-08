namespace FileSearchWebServer.Services;

public class LoggerService
{
    private readonly object _lock = new();

    public void Log(string message)
    {
        lock (_lock)
        {
            File.AppendAllText(
                "server.log",
                $"[{DateTime.Now}] {message}\n"
            );
        }
    }
}