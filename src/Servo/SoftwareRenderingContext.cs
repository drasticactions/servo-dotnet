namespace Servo;

public sealed class SoftwareRenderingContext : RenderingContext
{
    public unsafe SoftwareRenderingContext(uint width, uint height)
        : base((nint)ServoNative.rendering_context_new_software(width, height), nameof(SoftwareRenderingContext))
    {
    }
}
