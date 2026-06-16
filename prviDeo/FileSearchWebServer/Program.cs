using FileSearchWebServer;

public class Program
{
    private const string Url = "http://localhost";
    private const int Port = 5050;
    private const int CacheSize = 3;
    private const int CacheEntryExpirationSeconds = 60;

    private static void Main(string[] args)
    {
        string baseDir = AppContext.BaseDirectory;

        // Vracamo se tri nivoa unazad da dodjemo do projektnog foldera.
        string projectDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));
        string rootDir = Path.Combine(projectDir, "root");
        string logPath = Path.Combine(projectDir, "logs", "server.log");

        Directory.CreateDirectory(rootDir);

        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        WebServer server = new WebServer(
            Url,
            Port,
            rootDir,
            logPath,
            CacheSize,
            TimeSpan.FromSeconds(CacheEntryExpirationSeconds));

        // Posebna nit za graceful shutdown.
        Thread shutdownThread = new Thread(() =>
        {
            
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(true);

                // Ako je pritisnut ESC, pokrecemo kontrolisano gasenje servera.
                if (key.Key == ConsoleKey.Escape)
                {
                    Console.WriteLine("ESC pritisnut. Pokrece se graceful shutdown...");
                    server.Stop();

                    break;
                }
            }
        });

        // Background nit ne sprecava proces da se zavrsi.
        // Ako sve foreground niti zavrse, proces moze da se ugasi.
        shutdownThread.IsBackground = true;

        shutdownThread.Start();

        Console.WriteLine("Server se pokrece. Za gasenje pritisni ESC.");

        // Pokretanje glavnog servera
        server.Run();

        Console.WriteLine("Server je zavrsio rad.");
    }
}
