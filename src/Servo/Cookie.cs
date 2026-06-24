using System.Text.Json;

namespace Servo;

/// <summary>A cookie returned by <see cref="ServoEngine.GetCookiesForUrl"/>.</summary>
public sealed class Cookie(
    string name,
    string value,
    string? domain,
    string? path,
    bool? secure,
    bool? httpOnly,
    string? sameSite,
    long? expiresUnixSeconds,
    long? maxAgeSeconds)
{
    public string Name { get; } = name;
    public string Value { get; } = value;
    public string? Domain { get; } = domain;
    public string? Path { get; } = path;
    public bool? Secure { get; } = secure;
    public bool? HttpOnly { get; } = httpOnly;
    public string? SameSite { get; } = sameSite;

    /// <summary>Expiry as a Unix timestamp in seconds, or null for a session cookie.</summary>
    public long? ExpiresUnixSeconds { get; } = expiresUnixSeconds;

    /// <summary>Max-Age in seconds, or null if not set.</summary>
    public long? MaxAgeSeconds { get; } = maxAgeSeconds;

    internal static IReadOnlyList<Cookie> ParseJson(string json)
    {
        var result = new List<Cookie>();
        using var doc = JsonDocument.Parse(json);
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            result.Add(new Cookie(
                name: e.GetProperty("name").GetString() ?? "",
                value: e.GetProperty("value").GetString() ?? "",
                domain: GetStringOrNull(e, "domain"),
                path: GetStringOrNull(e, "path"),
                secure: GetBoolOrNull(e, "secure"),
                httpOnly: GetBoolOrNull(e, "httpOnly"),
                sameSite: GetStringOrNull(e, "sameSite"),
                expiresUnixSeconds: GetLongOrNull(e, "expires"),
                maxAgeSeconds: GetLongOrNull(e, "maxAge")));
        }
        return result;

        static string? GetStringOrNull(JsonElement e, string name) =>
            e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

        static bool? GetBoolOrNull(JsonElement e, string name) =>
            e.TryGetProperty(name, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? p.GetBoolean() : null;

        static long? GetLongOrNull(JsonElement e, string name) =>
            e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt64() : null;
    }
}
