

namespace Servo;

public sealed class FaviconData
{
    public byte[] Data { get; }
    public uint Width { get; }
    public uint Height { get; }
    public PixelFormat Format { get; }

    internal FaviconData(byte[] data, uint width, uint height, PixelFormat format)
    {
        Data = data;
        Width = width;
        Height = height;
        Format = format;
    }
}
