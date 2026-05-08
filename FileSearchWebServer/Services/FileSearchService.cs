namespace FileSearchWebServer.Services;

public class FileSearchService
{
    private readonly string _rootPath;

    public FileSearchService()
    {
        _rootPath = Path.Combine(Directory.GetCurrentDirectory(), "Files");
    }

    public List<string> Search(string keyword)
    {
        return Directory
            .GetFiles(_rootPath)
            .Where(f => Path.GetFileName(f)
            .Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public string? GetFile(string fileName)
    {
        var path = Path.Combine(_rootPath, fileName);

        return File.Exists(path) ? path : null;
    }
}