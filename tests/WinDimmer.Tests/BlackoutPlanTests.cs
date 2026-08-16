using WinDimmer;
using Xunit;

public class BlackoutPlanTests
{
    [Fact]
    public void Undimmed_window_starts_a_new_blackout_session()
    {
        Assert.Equal(BlackoutOp.StartNew, BlackoutPlan.Next(isDimmed: false, memory: null));
    }

    [Fact]
    public void Dimmed_window_without_memory_is_covered()
    {
        Assert.Equal(BlackoutOp.Cover, BlackoutPlan.Next(isDimmed: true, memory: null));
    }

    [Fact]
    public void Blackout_created_session_is_released_entirely()
    {
        // 원래 디밍하지 않던 창 — 해제해서 흔적을 없앤다 (설계 §2 케이스 3)
        var memory = new BlackoutMemory(0, false, CreatedByBlackout: true);
        Assert.Equal(BlackoutOp.Release, BlackoutPlan.Next(isDimmed: true, memory));
    }

    [Theory]
    [InlineData(110, false)]
    [InlineData(110, true)]
    [InlineData(0, false)]     // 이전 알파가 경계값이어도 복원 판정은 같다
    [InlineData(255, true)]    // 이미 255로 디밍하던 창을 가렸다 되돌리는 경우
    public void Previously_dimmed_window_is_restored(byte prevAlpha, bool prevCustom)
    {
        var memory = new BlackoutMemory(prevAlpha, prevCustom, CreatedByBlackout: false);
        Assert.Equal(BlackoutOp.Restore, BlackoutPlan.Next(isDimmed: true, memory));
    }

    [Fact]
    public void Cleared_memory_covers_again_with_fresh_state()
    {
        // 가림 중 밝기를 직접 바꾸면 기억이 소거된다(세션의 SetAlpha가 담당).
        // 그 뒤의 토글은 "가림 아님"으로 판정되어 그 시점 밝기를 새로 기억해야 한다.
        Assert.Equal(BlackoutOp.Cover, BlackoutPlan.Next(isDimmed: true, memory: null));
    }

    [Fact]
    public void Cover_alpha_is_fully_opaque()
    {
        Assert.Equal(byte.MaxValue, BlackoutPlan.CoverAlpha);
    }
}
