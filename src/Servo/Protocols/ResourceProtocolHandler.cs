using System.IO;

namespace Servo.Protocols;

public class ResourceProtocolHandler : IProtocolHandler
{
    private readonly string _resourceDir;

    public ResourceProtocolHandler(string resourceDir)
    {
        _resourceDir = resourceDir;
    }

    public ProtocolResponse? Load(string url)
    {
        // Extract path from resource: URL (ex. "resource:///newtab.css" -> "/newtab.css")
        var uri = new Uri(url);
        var path = uri.AbsolutePath;

        if (string.IsNullOrEmpty(path) || !path.StartsWith('/') || path.Contains(".."))
            return null;

        var filePath = Path.GetFullPath(Path.Combine(_resourceDir, path.TrimStart('/')));

        // Ensure the resolved path is still within the resource directory
        if (!filePath.StartsWith(Path.GetFullPath(_resourceDir)))
            return null;

        if (!File.Exists(filePath) || (File.GetAttributes(filePath) & FileAttributes.Directory) != 0)
            return null;

        var body = File.ReadAllBytes(filePath);
        var contentType = GetMimeType(filePath);
        return new ProtocolResponse(body, contentType);
    }

    private static string GetMimeType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".json" => "application/json",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".txt" => "text/plain",
            ".xml" => "application/xml",
            _ => "application/octet-stream",
        };
    }
}
