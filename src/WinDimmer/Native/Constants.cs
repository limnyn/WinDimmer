namespace WinDimmer.Native;

internal static class Constants
{
    // 창 스타일
    internal const int WS_POPUP = unchecked((int)0x80000000);
    internal const int WS_EX_LAYERED = 0x00080000;
    internal const int WS_EX_TRANSPARENT = 0x00000020;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;
    internal const int WS_EX_NOACTIVATE = 0x08000000;
    internal const int WS_EX_APPWINDOW = 0x00040000;
    // 진단용. TOPMOST 대상은 일반(비-TOPMOST) 오버레이가 절대 위로 올라갈 수 없다.
    internal const int WS_EX_TOPMOST = 0x00000008;

    // 레이어드 윈도우
    internal const uint LWA_ALPHA = 0x00000002;

    // SetWindowPos 플래그
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_SHOWWINDOW = 0x0040;
    internal const uint SWP_HIDEWINDOW = 0x0080;

    // GetWindowLongPtr / SetWindowLongPtr 인덱스
    internal const int GWLP_HWNDPARENT = -8;
    internal const int GWL_EXSTYLE = -20;

    // GetWindow 방향
    internal const uint GW_HWNDNEXT = 2;
    internal const uint GW_HWNDPREV = 3;
    internal const uint GW_OWNER = 4;

    // GetAncestor 플래그
    internal const uint GA_ROOT = 2;

    // DWM 속성
    internal const int DWMWA_CLOAKED = 14;
    internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    // WinEvent
    internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    internal const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    internal const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    internal const uint EVENT_OBJECT_SHOW = 0x8002;
    internal const uint EVENT_OBJECT_DESTROY = 0x8001;
    internal const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    internal const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    internal const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    internal const int OBJID_WINDOW = 0;
    internal const int CHILDID_SELF = 0;

    // 저수준 훅
    internal const int WH_MOUSE_LL = 14;
    internal const int WM_LBUTTONDOWN = 0x0201;

    // 핫키
    internal const int WM_HOTKEY = 0x0312;
    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint MOD_NOREPEAT = 0x4000;
    internal const int VK_ESCAPE = 0x1B;
    // 좌우를 구분하지 않는 수정키 코드. GetAsyncKeyState에 넘기면 좌우 어느 쪽이든 눌려 있으면 잡힌다.
    internal const int VK_SHIFT = 0x10;
    internal const int VK_CONTROL = 0x11;
    internal const int VK_MENU = 0x12;   // Alt

    // 메시지 전용 창
    internal static readonly IntPtr HWND_MESSAGE = new(-3);

    // 프로세스 접근
    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const int ERROR_ACCESS_DENIED = 5;

    // 시스템 메트릭
    internal const int SM_CXSMICON = 49;

    // 마우스 메시지 (Task 3에서 사용)
    internal const int WM_MOUSEMOVE = 0x0200;
}
