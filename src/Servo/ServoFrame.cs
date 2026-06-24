namespace Servo;

/// <summary>
/// How a <see cref="RenderingContext"/> exposes rendered frames as native GPU surfaces
/// (see <see cref="RenderingContext.FrameExportKind"/>).
/// </summary>
public enum ServoFrameExportKind
{
    /// <summary>Frames can only be read back as pixels (<see cref="RenderingContext.ReadPixels"/>).</summary>
    None = 0,

    /// <summary>Frames are exported as retained IOSurfaceRef handles.</summary>
    IOSurface = 1,

    /// <summary>Frames are exported as D3D11 shared handles.</summary>
    D3D11SharedHandle = 2,
}

/// <summary>
/// A rendered frame taken out of the rendering context's swap chain, exposed by its
/// native GPU surface handle. The handle stays valid until the frame is returned with
/// <see cref="RenderingContext.ReleaseFrame"/>; release it only once the compositor has
/// stopped reading from it (ex. when a newer frame has replaced it on screen).
/// </summary>
public readonly record struct ServoFrame(ulong Id, nint NativeHandle, uint Width, uint Height);
