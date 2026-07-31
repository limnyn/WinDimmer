using WinDimmer;
using Xunit;

public class AppWindowFilterTests
{
    // 카카오톡 메인 창: owner 없음, 제목 있음, WS_EX_APPWINDOW
    [Fact]
    public void KakaoTalk_main_window_is_user_window()
    {
        var w = new WindowKind(IsVisible: true, HasOwner: false, IsToolWindow: false,
            IsAppWindow: true, HasTitle: true);
        Assert.True(AppWindowFilter.IsUserWindow(w));
    }

    // 카카오톡 채팅창: owner 없음, 제목 있음, WS_EX_APPWINDOW 아님
    [Fact]
    public void KakaoTalk_chat_window_is_user_window()
    {
        var w = new WindowKind(IsVisible: true, HasOwner: false, IsToolWindow: false,
            IsAppWindow: false, HasTitle: true);
        Assert.True(AppWindowFilter.IsUserWindow(w));
    }

    // 광고 배너: owner 있음, 제목 없음 — 이게 이번 결함의 핵심 사례다
    [Fact]
    public void Ad_banner_is_not_user_window()
    {
        var w = new WindowKind(IsVisible: true, HasOwner: true, IsToolWindow: false,
            IsAppWindow: false, HasTitle: false);
        Assert.False(AppWindowFilter.IsUserWindow(w));
    }

    [Fact]
    public void Invisible_window_is_not_user_window()
    {
        var w = new WindowKind(IsVisible: false, HasOwner: false, IsToolWindow: false,
            IsAppWindow: true, HasTitle: true);
        Assert.False(AppWindowFilter.IsUserWindow(w));
    }

    [Fact]
    public void Tool_window_is_not_user_window()
    {
        var w = new WindowKind(IsVisible: true, HasOwner: false, IsToolWindow: true,
            IsAppWindow: false, HasTitle: true);
        Assert.False(AppWindowFilter.IsUserWindow(w));
    }

    // 소유 창이라도 WS_EX_APPWINDOW를 선언하면 진짜 창으로 인정한다
    [Fact]
    public void Owned_window_with_appwindow_flag_is_user_window()
    {
        var w = new WindowKind(IsVisible: true, HasOwner: true, IsToolWindow: false,
            IsAppWindow: true, HasTitle: true);
        Assert.True(AppWindowFilter.IsUserWindow(w));
    }

    // owner는 없지만 제목도 없는 창 — 사용자에게 의미 있는 창이 아니다
    [Fact]
    public void Untitled_ownerless_window_is_not_user_window()
    {
        var w = new WindowKind(IsVisible: true, HasOwner: false, IsToolWindow: false,
            IsAppWindow: false, HasTitle: false);
        Assert.False(AppWindowFilter.IsUserWindow(w));
    }
}
