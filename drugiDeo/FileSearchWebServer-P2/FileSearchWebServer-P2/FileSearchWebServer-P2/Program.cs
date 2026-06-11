using FileSearchWebServer;

public class Program
{
    private const string Url = "http://localhost";
    private const int Port = 5050;
    private const int WorkerCount = 8;
    private const int CacheSize = 3;

    private static void Main(string[] args)
    {
        string baseDir = AppContext.BaseDirectory;
        string projectDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));
        string rootDir = Path.Combine(projectDir, "root");
        string logPath = Path.Combine(projectDir, "logs", "server.log");

        Directory.CreateDirectory(rootDir);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        WebServer server = new WebServer(Url, Port, rootDir, logPath, WorkerCount, CacheSize);

        // Taskovi se startuju iz Main-a, a Main ostaje sinhron jer nema potrebe
        // da entry point bude async.
        server.StartWorkers();

        // Console.ReadKey je blokirajuca konzolna operacija, pa je ovde
        // klasicna nit jednostavnije i smislenije resenje od Task-a.
        Thread shutdownThread = new Thread(() =>
        {
            try
            {
                while (true)
                {
                    ConsoleKeyInfo key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Escape)
                    {
                        Console.WriteLine("ESC pritisnut. Pokrece se graceful shutdown...");
                        server.Stop();
                        break;
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // Test pokretanje sa redirektovanim output-om nema interaktivnu konzolu.
            }
        });

        shutdownThread.IsBackground = true;
        shutdownThread.Start();

        Console.WriteLine("Server se pokrece. Za gasenje pritisni ESC.");
        server.Run();
        Console.WriteLine("Server je zavrsio rad.");
    }
}
