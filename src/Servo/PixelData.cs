namespace Servo;

public sealed class PixelData
{
    public byte[] Data { get; }

    public uint Width { get; }

    public uint Height { get; }

    public int Stride => (int)(Width * 4);

    internal PixelData(byte[] data, uint width, uint height)
    {
        Data = data;
        Width = width;
        Height = height;
    }
}
