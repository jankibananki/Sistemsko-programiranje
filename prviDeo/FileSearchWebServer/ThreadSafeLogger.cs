namespace FileSearchWebServer;


public class ThreadSafeLogger
{
    private readonly string _logPath;
    private readonly object _lock = new object();

    public ThreadSafeLogger(string logPath)
    {
        _logPath = logPath;
    }

    public void Log(string message)
    {
        
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Thread {Thread.CurrentThread.ManagedThreadId}] {message}";

        lock (_lock)
        {

            File.AppendAllText(_logPath, line + Environment.NewLine);
        }

        Console.WriteLine(line);
    }

    public void LogException(Exception ex)
    {
        Log("GRESKA: " + ex.GetType().Name + " - " + ex.Message);
    }
}
