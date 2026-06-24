using System.Text.Json;

namespace Servo;

public sealed class BluetoothDevice
{
    public string Name { get; }
    public string Address { get; }

    public BluetoothDevice(string name, string address)
    {
        Name = name;
        Address = address;
    }
}

public sealed class BluetoothDeviceSelectionEventArgs : EventArgs
{
    private readonly nuint _handle;
    private bool _responded;

    public IReadOnlyList<BluetoothDevice> Devices { get; }

    internal BluetoothDeviceSelectionEventArgs(IReadOnlyList<BluetoothDevice> devices, nuint handle)
    {
        Devices = devices;
        _handle = handle;
    }

    public void PickDevice(int index)
    {
        if (_responded) return;
        _responded = true;
        ServoNative.bluetooth_device_pick(_handle, (nuint)index);
    }

    public void Cancel()
    {
        if (_responded) return;
        _responded = true;
        ServoNative.bluetooth_device_cancel(_handle);
    }

    internal static IReadOnlyList<BluetoothDevice> ParseDevicesJson(string json)
    {
        var result = new List<BluetoothDevice>();
        using var doc = JsonDocument.Parse(json);
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var name = element.GetProperty("name").GetString() ?? "";
            var address = element.GetProperty("address").GetString() ?? "";
            result.Add(new BluetoothDevice(name, address));
        }
        return result;
    }
}
