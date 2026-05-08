namespace FileSearchWebServer.Models;

public class CacheItem
{
    public string Key { get; set; } = string.Empty;
    public string HtmlResponse { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}