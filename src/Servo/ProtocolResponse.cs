namespace Servo;

public sealed class ProtocolResponse(byte[] body, string contentType, ushort statusCode = 200)
{
    public byte[] Body { get; } = body ?? throw new ArgumentNullException(nameof(body));

    public string ContentType { get; } = contentType ?? throw new ArgumentNullException(nameof(contentType));

    public ushort StatusCode { get; } = statusCode;
}
