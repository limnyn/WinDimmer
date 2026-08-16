using WinDimmer;
using Xunit;

public class DimOverridePlanTests
{
    private static OverrideState State(OverrideKind kind, bool created = false, byte prev = 110, bool prevCustom = false) =>
        new(kind, new OverrideMemory(prev, prevCustom, created));

    // --- 미디밍 창 ---

    [Fact]
    public void Cover_starts_a_session_on_undimmed_window()
    {
        Assert.Equal(OverrideOp.Start, DimOverridePlan.Next(OverrideKind.Cover, isDimmed: false, current: null));
    }

    [Fact]
    public void Lift_does_nothing_on_undimmed_window()
    {
        // 걷을 필터가 없는 창에 알파 0 세션을 만드는 것은 상태 오염이다 (부록 A)
        Assert.Equal(OverrideOp.None, DimOverridePlan.Next(OverrideKind.Lift, isDimmed: false, current: null));
    }

    // --- 디밍 중, 오버라이드 없음 ---

    [Theory]
    [InlineData(OverrideKind.Cover)]
    [InlineData(OverrideKind.Lift)]
    public void Dimmed_window_enters_the_pressed_direction(OverrideKind pressed)
    {
        Assert.Equal(OverrideOp.Enter, DimOverridePlan.Next(pressed, isDimmed: true, current: null));
    }

    // --- 같은 키 = 복원, 반대 키 = 전환 ---

    [Theory]
    [InlineData(OverrideKind.Cover)]
    [InlineData(OverrideKind.Lift)]
    public void Same_key_restores_previous_state(OverrideKind kind)
    {
        Assert.Equal(OverrideOp.Restore, DimOverridePlan.Next(kind, isDimmed: true, State(kind)));
    }

    [Theory]
    [InlineData(OverrideKind.Cover, OverrideKind.Lift)]   // 가림 중 ← → 즉시 걷기
    [InlineData(OverrideKind.Lift, OverrideKind.Cover)]   // 걷기 중 → → 즉시 가림
    public void Opposite_key_switches_extremes_keeping_memory(OverrideKind current, OverrideKind pressed)
    {
        Assert.Equal(OverrideOp.Switch, DimOverridePlan.Next(pressed, isDimmed: true, State(current)));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(255, true)]   // 경계값 기억이어도 판정은 같다
    public void Restore_decision_ignores_memory_contents(byte prev, bool prevCustom)
    {
        var state = State(OverrideKind.Cover, prev: prev, prevCustom: prevCustom);
        Assert.Equal(OverrideOp.Restore, DimOverridePlan.Next(OverrideKind.Cover, isDimmed: true, state));
    }

    // --- 오버라이드가 만든 세션 ---

    [Theory]
    [InlineData(OverrideKind.Cover)]
    [InlineData(OverrideKind.Lift)]
    public void Override_created_session_is_released_by_either_key(OverrideKind pressed)
    {
        // 원본이 미디밍 상태이므로 해제가 곧 복원이다. 이 세션이 걷기로 전환되는 일은 없다.
        var created = State(OverrideKind.Cover, created: true);
        Assert.Equal(OverrideOp.Release, DimOverridePlan.Next(pressed, isDimmed: true, created));
    }

    // --- 기억 소거 후 ---

    [Fact]
    public void Cleared_memory_enters_again_with_fresh_state()
    {
        // 오버라이드 중 밝기를 직접 바꾸면 기억이 소거된다(세션 SetAlpha 담당).
        // 그 뒤의 토글은 "오버라이드 아님"으로 판정되어 그 시점 밝기를 새로 기억해야 한다.
        Assert.Equal(OverrideOp.Enter, DimOverridePlan.Next(OverrideKind.Cover, isDimmed: true, current: null));
    }

    // --- 상수 ---

    [Fact]
    public void Extreme_alphas_are_opaque_and_transparent()
    {
        Assert.Equal(byte.MaxValue, DimOverridePlan.CoverAlpha);
        Assert.Equal((byte)0, DimOverridePlan.LiftAlpha);
        Assert.Equal(DimOverridePlan.CoverAlpha, DimOverridePlan.AlphaFor(OverrideKind.Cover));
        Assert.Equal(DimOverridePlan.LiftAlpha, DimOverridePlan.AlphaFor(OverrideKind.Lift));
    }
}
