namespace WinDimmer;

/// <summary>자동 매칭 대상인지 판정하는 데 필요한 창의 성질. Win32 조회 결과를 담아 넘긴다.</summary>
public readonly record struct WindowKind(
    bool IsVisible,
    bool HasOwner,
    bool IsToolWindow,
    bool IsAppWindow,
    bool HasTitle);

/// <summary>
/// 규칙·프로세스 기억이 자동으로 어둡게 할 '진짜 앱 창'인지 판정한다.
/// 광고 배너나 팝업 같은 보조 창까지 대상이 되면 화면 일부만 검게 칠해진다.
/// </summary>
public static class AppWindowFilter
{
    public static bool IsUserWindow(WindowKind w)
    {
        // 1. 보이지 않으면 대상이 아니다.
        if (!w.IsVisible) return false;

        // 2. 툴 창(WS_EX_TOOLWINDOW)은 늘 보조 창이다.
        if (w.IsToolWindow) return false;

        // 3. WS_EX_APPWINDOW는 앱이 스스로 "진짜 창"이라고 선언한 것이므로
        //    아래의 소유 창 판정보다 우선한다.
        if (w.IsAppWindow) return true;

        // 4. 소유 창(owner가 있는 창)은 광고 배너·팝업 등 보조 창이다.
        //    GA_ROOT만으로는 걸러지지 않는다 — 소유 창은 자기 자신이 GA_ROOT이기 때문이다.
        if (w.HasOwner) return false;

        // 5. 제목이 없으면 사용자에게 의미 있는 창이 아니다.
        if (!w.HasTitle) return false;

        return true;
    }
}
