using WinDimmer;
using Xunit;

public class ZOrderPlanTests
{
    private static readonly IntPtr Target = new(100);
    private static readonly IntPtr Overlay = new(200);
    private static readonly IntPtr Other = new(300);
    private static readonly IntPtr Banner = new(400);
    private static readonly IntPtr Helper = new(500);

    private static ZWindow Owned(IntPtr handle) => new(handle, Target, true);
    private static ZWindow Free(IntPtr handle) => new(handle, IntPtr.Zero, true);
    private static ZWindow Invisible(IntPtr handle) => new(handle, IntPtr.Zero, false);

    [Fact]
    public void The_first_unrelated_visible_window_becomes_the_anchor()
    {
        var above = new[] { Free(Other) };

        Assert.Equal(Other, ZOrderPlan.InsertAfter(above, Target, Overlay, reachedTop: true));
    }

    [Fact]
    public void Windows_owned_by_the_target_are_stepped_over()
    {
        // 광고 배너가 대상 위에 얹혀 있는 상황. 배너 위의 남의 창이 기준이 돼야
        // 오버레이가 배너까지 덮는다.
        var above = new[] { Owned(Banner), Free(Other) };

        Assert.Equal(Other, ZOrderPlan.InsertAfter(above, Target, Overlay, reachedTop: true));
    }

    [Fact]
    public void The_overlay_itself_is_never_the_anchor()
    {
        // 이미 제자리에 있는 상태. 자기 자신을 기준으로 삼으면 잘못된 SetWindowPos 호출이 된다.
        var above = new[] { Owned(Banner), new ZWindow(Overlay, IntPtr.Zero, true), Free(Other) };

        Assert.Equal(Other, ZOrderPlan.InsertAfter(above, Target, Overlay, reachedTop: true));
    }

    [Fact]
    public void Invisible_helper_windows_are_stepped_over()
    {
        var above = new[] { Invisible(Helper), Free(Other) };

        Assert.Equal(Other, ZOrderPlan.InsertAfter(above, Target, Overlay, reachedTop: true));
    }

    [Fact]
    public void Nothing_above_the_group_means_the_very_top()
    {
        var above = new[] { Owned(Banner), Invisible(Helper) };

        Assert.Equal(IntPtr.Zero, ZOrderPlan.InsertAfter(above, Target, Overlay, reachedTop: true));
    }

    [Fact]
    public void An_empty_list_that_reached_the_top_means_the_very_top()
    {
        Assert.Equal(
            IntPtr.Zero,
            ZOrderPlan.InsertAfter(Array.Empty<ZWindow>(), Target, Overlay, reachedTop: true));
    }

    [Fact]
    public void A_truncated_walk_with_no_anchor_leaves_the_z_order_alone()
    {
        // 안전 상한에 걸려 끊긴 목록. "위에 아무것도 없다"고 결론지으면 오버레이를 화면 맨 위로
        // 올려 다른 프로그램까지 덮어 버린다. 판단을 미루는 쪽이 안전하다.
        var above = new[] { Owned(Banner) };

        Assert.Null(ZOrderPlan.InsertAfter(above, Target, Overlay, reachedTop: false));
    }

    [Fact]
    public void A_truncated_walk_still_answers_when_it_found_an_anchor()
    {
        var above = new[] { Owned(Banner), Free(Other) };

        Assert.Equal(Other, ZOrderPlan.InsertAfter(above, Target, Overlay, reachedTop: false));
    }

    [Fact]
    public void A_zero_target_is_rejected()
    {
        var above = new[] { Free(Other) };

        Assert.Null(ZOrderPlan.InsertAfter(above, IntPtr.Zero, Overlay, reachedTop: true));
    }

    [Fact]
    public void IsBoundary_agrees_with_what_InsertAfter_picks()
    {
        // 훑는 쪽이 이 판정으로 조기 종료하므로, 둘이 어긋나면 오버레이가 엉뚱한 자리에 꽂힌다.
        var above = new[] { Owned(Banner), Invisible(Helper), Free(Other), Free(new IntPtr(600)) };

        IntPtr? picked = ZOrderPlan.InsertAfter(above, Target, Overlay, reachedTop: true);
        ZWindow first = above.First(w => ZOrderPlan.IsBoundary(w, Target, Overlay));

        Assert.Equal(first.Handle, picked);
    }

    [Fact]
    public void IsBoundary_rejects_the_overlay_owned_and_invisible_windows()
    {
        Assert.False(ZOrderPlan.IsBoundary(new ZWindow(Overlay, IntPtr.Zero, true), Target, Overlay));
        Assert.False(ZOrderPlan.IsBoundary(Owned(Banner), Target, Overlay));
        Assert.False(ZOrderPlan.IsBoundary(Invisible(Helper), Target, Overlay));
        Assert.True(ZOrderPlan.IsBoundary(Free(Other), Target, Overlay));
    }

    [Fact]
    public void A_window_owned_by_somebody_else_is_a_boundary()
    {
        // 다른 프로그램의 대화상자. 대상의 그룹이 아니므로 그 아래에 머물러야 한다.
        var above = new[] { new ZWindow(Other, new IntPtr(999), true) };

        Assert.Equal(Other, ZOrderPlan.InsertAfter(above, Target, Overlay, reachedTop: true));
    }
}
