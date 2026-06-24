namespace Servo;

/// <summary>The API surface a cookie query/operation originates from.</summary>
public enum CookieSource : byte
{
    /// <summary>An HTTP API.</summary>
    Http = 0,

    /// <summary>A non-HTTP API (e.g. a script reading document.cookie).</summary>
    NonHttp = 1,
}
