namespace Servo;

public sealed class HardwareRenderingContext : RenderingContext
{
    public unsafe HardwareRenderingContext(uint width, uint height)
        : base((nint)ServoNative.rendering_context_new_hardware(width, height), nameof(HardwareRenderingContext))
    {
    }
}
