using System.Runtime.InteropServices;
using System.Text.Json;

namespace Servo;

public sealed class FilePickerRequestEventArgs : EventArgs
{
    private readonly nuint _handle;
    private bool _responded;

    internal FilePickerRequestEventArgs(
        IReadOnlyList<string> filterPatterns,
        bool allowMultiple,
        IReadOnlyList<string> currentPaths,
        nuint handle)
    {
        FilterPatterns = filterPatterns;
        AllowMultiple = allowMultiple;
        CurrentPaths = currentPaths;
        _handle = handle;
    }

    public IReadOnlyList<string> FilterPatterns { get; }
    public bool AllowMultiple { get; }
    public IReadOnlyList<string> CurrentPaths { get; }

    public unsafe void Select(params string[] paths)
    {
        if (_responded) return;
        _responded = true;
        var ptrs = new nint[paths.Length];
        try
        {
            for (int i = 0; i < paths.Length; i++)
                ptrs[i] = Marshal.StringToCoTaskMemUTF8(paths[i]);
            fixed (nint* pPtrs = ptrs)
            {
                ServoNative.file_picker_select_and_submit(_handle, (byte**)pPtrs, (nuint)paths.Length);
            }
        }
        finally
        {
            foreach (var p in ptrs)
                if (p != 0) Marshal.FreeCoTaskMem(p);
        }
    }

    public unsafe void Dismiss()
    {
        if (_responded) return;
        _responded = true;
        ServoNative.file_picker_dismiss(_handle);
    }

    internal static IReadOnlyList<string> ParseJsonArray(string json)
    {
        return JsonSerializer.Deserialize(json, ServoJsonContext.Default.ListString) ?? [];
    }
}
