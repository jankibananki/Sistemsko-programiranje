namespace FileSearchWebServer;

public class FileSearchService
{
    private readonly string _rootDir;
    private readonly object _fileAccessLock = new object();

    public FileSearchService(string rootDir)
    {
        _rootDir = rootDir;
    }

    public List<string> SearchByKeyword(string keyword)
    {
        lock (_fileAccessLock)
        {
            return Directory
                .GetFiles(_rootDir, "*", SearchOption.TopDirectoryOnly)
                .Where(path => Path.GetFileName(path).Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .OrderBy(name => name)
                .ToList();
        }
    }

    // Proverava da li fajl za download postoji u root folderu.    
    public bool FileExistsInRoot(string fileName)
    {
        // GetSafePath sprecava path traversal napade.
        string fullPath = GetSafePath(fileName);

        lock (_fileAccessLock)
        {
            return File.Exists(fullPath);
        }
    }

    public byte[] ReadFile(string fileName)
    {
        string fullPath = GetSafePath(fileName);

        lock (_fileAccessLock)
        {
            return File.ReadAllBytes(fullPath);
        }
    }

    // Bezbedno pravi punu putanju do fajla.
    public string GetSafePath(string fileName)
    {
        
        string onlyFileName = Path.GetFileName(fileName);
        string fullPath = Path.GetFullPath(Path.Combine(_rootDir, onlyFileName));
        string rootFullPath = Path.GetFullPath(_rootDir);

        // Proveravamo da li dobijena putanja zaista pocinje root putanjom.
        if (!fullPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Nedozvoljen pristup fajlu van root direktorijuma.");
        }

        return fullPath;
    }
}
