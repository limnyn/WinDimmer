namespace WinDimmer;

public readonly record struct Rect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public readonly record struct WindowSnapshot(
    bool IsWindow,
    bool IsVisible,
    bool IsIconic,
    int Cloaked,
    Rect FrameBounds);

public enum OverlayVisibility
{
    Destroy,
    Hidden,
    Visible,
}

public readonly record struct DesiredState(OverlayVisibility Visibility, Rect Bounds)
{
    public static DesiredState Destroyed { get; } = new(OverlayVisibility.Destroy, default);
    public static DesiredState Hide { get; } = new(OverlayVisibility.Hidden, default);
    public static DesiredState Show(Rect bounds) => new(OverlayVisibility.Visible, bounds);
}

/// <summary>
/// 오버레이가 어떤 상태여야 하는지를 결정하는 순수 함수.
/// 어떤 이벤트가 오든 이 함수를 다시 돌려 결과를 적용하므로,
/// 훅이 몇 개를 놓쳐도 다음 이벤트에서 자동 복구된다.
/// </summary>
public static class WindowGeometry
{
    public static DesiredState Compute(WindowSnapshot s)
    {
        if (!s.IsWindow) return DesiredState.Destroyed;
        if (s.IsIconic) return DesiredState.Hide;
        if (s.Cloaked != 0) return DesiredState.Hide;
        if (!s.IsVisible) return DesiredState.Hide;
        if (s.FrameBounds.IsEmpty) return DesiredState.Hide;
        return DesiredState.Show(s.FrameBounds);
    }
}
