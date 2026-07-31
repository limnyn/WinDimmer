using WinDimmer;
using Xunit;

public class GeometryTests
{
    private static WindowSnapshot Healthy() =>
        new(IsWindow: true, IsVisible: true, IsIconic: false, Cloaked: 0,
            FrameBounds: new Rect(100, 100, 500, 400));

    [Fact]
    public void Healthy_window_is_visible_with_its_bounds()
    {
        var state = WindowGeometry.Compute(Healthy());
        Assert.Equal(OverlayVisibility.Visible, state.Visibility);
        Assert.Equal(new Rect(100, 100, 500, 400), state.Bounds);
    }

    [Fact]
    public void Dead_window_is_destroyed()
    {
        var state = WindowGeometry.Compute(Healthy() with { IsWindow = false });
        Assert.Equal(OverlayVisibility.Destroy, state.Visibility);
    }

    [Fact]
    public void Minimized_window_is_hidden()
    {
        var state = WindowGeometry.Compute(Healthy() with { IsIconic = true });
        Assert.Equal(OverlayVisibility.Hidden, state.Visibility);
    }

    [Fact]
    public void Cloaked_window_is_hidden()
    {
        var state = WindowGeometry.Compute(Healthy() with { Cloaked = 2 });
        Assert.Equal(OverlayVisibility.Hidden, state.Visibility);
    }

    [Fact]
    public void Invisible_window_is_hidden()
    {
        var state = WindowGeometry.Compute(Healthy() with { IsVisible = false });
        Assert.Equal(OverlayVisibility.Hidden, state.Visibility);
    }

    [Theory]
    [InlineData(100, 100, 100, 400)]   // 폭 0
    [InlineData(100, 100, 500, 100)]   // 높이 0
    [InlineData(500, 400, 100, 100)]   // 뒤집힌 rect
    public void Empty_bounds_are_hidden(int l, int t, int r, int b)
    {
        var state = WindowGeometry.Compute(Healthy() with { FrameBounds = new Rect(l, t, r, b) });
        Assert.Equal(OverlayVisibility.Hidden, state.Visibility);
    }

    [Fact]
    public void Dead_window_takes_priority_over_minimized()
    {
        var state = WindowGeometry.Compute(
            Healthy() with { IsWindow = false, IsIconic = true, Cloaked = 1 });
        Assert.Equal(OverlayVisibility.Destroy, state.Visibility);
    }
}
