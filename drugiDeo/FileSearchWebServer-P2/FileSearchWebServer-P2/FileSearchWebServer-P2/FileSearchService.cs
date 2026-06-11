namespace FileSearchWebServer;

public class FileSearchService
{
    private readonly string _rootDir;
    private readonly ReaderWriterLockSlim _fileLock = new ReaderWriterLockSlim();

    public FileSearchService(string rootDir)
    {
        _rootDir = Path.GetFullPath(rootDir);
    }

    public async Task<List<string>> SearchByKeywordAsync(string keyword, CancellationToken cancellationToken)
    {
        string[] fileNames;

        _fileLock.EnterReadLock();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            fileNames = Directory
                .GetFiles(_rootDir, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToArray();
        }
        finally
        {
            _fileLock.ExitReadLock();
        }

        // Filtriranje i sortiranje su CPU-bound deo posla. Task.Run ima smisla
        // ovde jer worker Task ne mora da zauzme istu nit dok se radi obrada.
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            return fileNames
                .Where(name => ContainsKeywordWithCpuWork(name, keyword, cancellationToken))
                .OrderBy(name => name)
                .ToList();
        }, cancellationToken);
    }

    public bool FileExistsInRoot(string fileName)
    {
        string fullPath = GetSafePath(fileName);

        _fileLock.EnterReadLock();
        try
        {
            return File.Exists(fullPath);
        }
        finally
        {
            _fileLock.ExitReadLock();
        }
    }

    public async Task<byte[]> ReadFileAsync(string fileName)
    {
        string fullPath = GetSafePath(fileName);

        _fileLock.EnterReadLock();
        try
        {
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Fajl ne postoji u root direktorijumu.", fileName);
        }
        finally
        {
            _fileLock.ExitReadLock();
        }

        return await File.ReadAllBytesAsync(fullPath);
    }

    public string GetSafePath(string fileName)
    {
        string onlyFileName = Path.GetFileName(fileName);
        string fullPath = Path.GetFullPath(Path.Combine(_rootDir, onlyFileName));
        string rootWithSeparator = _rootDir.EndsWith(Path.DirectorySeparatorChar)
            ? _rootDir
            : _rootDir + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Nedozvoljen pristup fajlu van root direktorijuma.");

        return fullPath;
    }

    private static bool ContainsKeywordWithCpuWork(string fileName, string keyword, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Namerno mala CPU-bound faza za demonstraciju: normalizuje poredjenje
        // i proverava substring bez menjanja funkcionalnog zahteva zadatka.
        return fileName.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }
}
