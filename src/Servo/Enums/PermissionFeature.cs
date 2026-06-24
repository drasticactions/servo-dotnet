

namespace Servo;

// Values must match the declaration order of servo's `PermissionFeature` enum
// (components/shared/embedder/lib.rs), which is what servo-ffi maps onto this byte.
public enum PermissionFeature : byte
{
    Geolocation = 0,
    Notifications = 1,
    Push = 2,
    Midi = 3,
    Camera = 4,
    Microphone = 5,
    Speaker = 6,
    DeviceInfo = 7,
    BackgroundSync = 8,
    Bluetooth = 9,
    PersistentStorage = 10,
    ScreenWakeLock = 11,
    Gamepad = 12,
}
