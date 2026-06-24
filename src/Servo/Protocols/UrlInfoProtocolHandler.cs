using System.Text;

namespace Servo.Protocols;

public class UrlInfoProtocolHandler : IProtocolHandler
{
    public bool IsFetchable => true;

    public bool IsSecure => true;

    public ProtocolResponse? Load(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        var sb = new StringBuilder();
        sb.AppendLine($"Full url: {url}");
        sb.AppendLine($"  scheme: {uri.Scheme}");
        sb.AppendLine($"    path: {uri.AbsolutePath}");
        sb.AppendLine($"   query: {uri.Query}");

        return new ProtocolResponse(Encoding.UTF8.GetBytes(sb.ToString()), "text/plain");
    }
}
