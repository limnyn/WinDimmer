using System.Runtime.InteropServices;
using WinDimmer.Native;
using static WinDimmer.Native.Constants;

namespace WinDimmer;

/// <summary>HWND에서 순수 판정 함수가 쓸 값들을 뽑아낸다.</summary>
internal static class WindowInspector
{
    public static WindowSnapshot Capture(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !User32.IsWindow(hwnd))
            return new WindowSnapshot(false, false, false, 0, default);

        int cloaked = 0;
        Dwmapi.DwmGetWindowAttributeInt(hwnd, DWMWA_CLOAKED, out cloaked, sizeof(int));

        Rect bounds = default;
        if (Dwmapi.DwmGetWindowAttribute(
                hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf<RECT>()) == 0)
        {
            bounds = r.ToRect();
        }

        return new WindowSnapshot(
            IsWindow: true,
            IsVisible: User32.IsWindowVisible(hwnd),
            IsIconic: User32.IsIconic(hwnd),
            Cloaked: cloaked,
            FrameBounds: bounds);
    }

    /// <summary>
    /// 대상이 상승 권한 프로세스인지 판정한다.
    /// PROCESS_QUERY_LIMITED_INFORMATION 조차 거부되면 우리보다 높은 무결성 수준이다.
    /// </summary>
    public static bool IsElevated(IntPtr hwnd)
    {
        User32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return false;

        IntPtr h = Kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero)
            return Marshal.GetLastWin32Error() == ERROR_ACCESS_DENIED;

        Kernel32.CloseHandle(h);
        return false;
    }

    /// <summary>
    /// Process.GetProcessById는 내부적으로 전체 프로세스 스냅샷을 찍어 비싸다.
    /// OpenProcess + QueryFullProcessImageName 두 syscall로 대체해 이 비용을 없앤다.
    /// </summary>
    public static string GetProcessName(IntPtr hwnd)
    {
        User32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return string.Empty;

        IntPtr h = Kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return string.Empty;

        try
        {
            var buffer = new char[512];
            uint size = (uint)buffer.Length;
            if (!Kernel32.QueryFullProcessImageName(h, 0, buffer, ref size))
                return string.Empty;

            string path = new string(buffer, 0, (int)size);
            return Path.GetFileNameWithoutExtension(path);
        }
        finally
        {
            Kernel32.CloseHandle(h);
        }
    }

    public static string GetTitle(IntPtr hwnd)
    {
        var buffer = new char[512];
        int len = User32.GetWindowText(hwnd, buffer, buffer.Length);
        return len > 0 ? new string(buffer, 0, len) : string.Empty;
    }

    /// <summary>
    /// AppWindowFilter 판정에 필요한 값들만 뽑아낸다.
    /// 전부 GetWindowLong / GetWindow / GetWindowTextLength 수준의 싼 호출이다 —
    /// 제목 문자열 전체를 복사하지 않고 길이만 물어본다.
    /// </summary>
    public static WindowKind GetKind(IntPtr hwnd)
    {
        int exStyle = (int)User32.GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        return new WindowKind(
            IsVisible: User32.IsWindowVisible(hwnd),
            HasOwner: User32.GetWindow(hwnd, GW_OWNER) != IntPtr.Zero,
            IsToolWindow: (exStyle & WS_EX_TOOLWINDOW) != 0,
            IsAppWindow: (exStyle & WS_EX_APPWINDOW) != 0,
            HasTitle: User32.GetWindowTextLength(hwnd) > 0);
    }

    /// <summary>진단용. 확장 스타일 원값. TOPMOST 여부 같은 것을 눈으로 확인하는 데 쓴다.</summary>
    public static int GetExStyle(IntPtr hwnd) => (int)User32.GetWindowLongPtr(hwnd, GWL_EXSTYLE);

    /// <summary>진단용. 이 창의 소유자. 없으면 <see cref="IntPtr.Zero"/>.</summary>
    public static IntPtr GetOwner(IntPtr hwnd) => User32.GetWindow(hwnd, GW_OWNER);

    /// <summary>창을 소유한 프로세스 ID. 조회 실패 시 0.</summary>
    public static uint GetProcessId(IntPtr hwnd)
    {
        User32.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid;
    }
}
