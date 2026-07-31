using System.Runtime.InteropServices;

namespace WinDimmer.Native;

internal static partial class Dwmapi
{
    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(
        IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    internal static partial int DwmGetWindowAttributeInt(
        IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
}
