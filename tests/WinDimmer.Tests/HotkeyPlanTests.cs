using WinDimmer;
using Xunit;

public class HotkeyPlanTests
{
    private static HotkeySpec Spec(Keys keys) => new(keys);

    private static readonly HotkeySpec ToggleDesired = new(Keys.Control | Keys.Alt | Keys.Q);
    private static readonly HotkeySpec TogglePrevious = new(Keys.Control | Keys.Alt | Keys.D);
    private static readonly HotkeySpec BrighterDesired = new(Keys.Control | Keys.Alt | Keys.Up);
    private static readonly HotkeySpec DarkerDesired = new(Keys.Control | Keys.Alt | Keys.Down);
    private static readonly HotkeySpec ClearAllDesired = new(Keys.Control | Keys.Alt | Keys.D);

    [Fact]
    public void No_failures_returns_desired_unchanged()
    {
        var desired = new Dictionary<HotkeyAction, HotkeySpec>
        {
            [HotkeyAction.Toggle] = ToggleDesired,
            [HotkeyAction.ClearAll] = ClearAllDesired,
        };
        var previous = new Dictionary<HotkeyAction, HotkeySpec>();

        var result = HotkeyPlan.Reconcile(desired, previous, failed: Array.Empty<HotkeyAction>());

        Assert.Equal(2, result.Count);
        Assert.Equal(ToggleDesired, result[HotkeyAction.Toggle]);
        Assert.Equal(ClearAllDesired, result[HotkeyAction.ClearAll]);
    }

    [Fact]
    public void One_failure_substitutes_its_previous_combination()
    {
        var desired = new Dictionary<HotkeyAction, HotkeySpec>
        {
            [HotkeyAction.Toggle] = ToggleDesired,
            [HotkeyAction.Brighter] = BrighterDesired,
        };
        var previous = new Dictionary<HotkeyAction, HotkeySpec>
        {
            [HotkeyAction.Toggle] = TogglePrevious,
        };

        var result = HotkeyPlan.Reconcile(desired, previous, failed: new[] { HotkeyAction.Toggle });

        Assert.Equal(TogglePrevious, result[HotkeyAction.Toggle]);
        Assert.Equal(BrighterDesired, result[HotkeyAction.Brighter]);
    }

    [Fact]
    public void Failure_with_no_previous_entry_is_removed()
    {
        var desired = new Dictionary<HotkeyAction, HotkeySpec>
        {
            [HotkeyAction.Toggle] = ToggleDesired,
            [HotkeyAction.Brighter] = BrighterDesired,
        };
        var previous = new Dictionary<HotkeyAction, HotkeySpec>();   // 이전 값 없음 — 예: 시작 시점

        var result = HotkeyPlan.Reconcile(desired, previous, failed: new[] { HotkeyAction.Toggle });

        Assert.False(result.ContainsKey(HotkeyAction.Toggle));
        Assert.Equal(BrighterDesired, result[HotkeyAction.Brighter]);
    }

    [Fact]
    public void Substitution_colliding_with_another_actions_desired_combination_is_dropped()
    {
        // Toggle이 Ctrl+Alt+Q(다른 앱 소유)로 바뀌려다 실패해 이전 값 Ctrl+Alt+D로 되돌아가는데,
        // 마침 ClearAll이 이번에 Ctrl+Alt+D를 새로 요청했다. 되돌린 값이 새 요청을 밀어낼 수는 없다.
        var desired = new Dictionary<HotkeyAction, HotkeySpec>
        {
            [HotkeyAction.Toggle] = ToggleDesired,
            [HotkeyAction.ClearAll] = ClearAllDesired,   // Ctrl+Alt+D — Toggle의 이전 값과 동일
        };
        var previous = new Dictionary<HotkeyAction, HotkeySpec>
        {
            [HotkeyAction.Toggle] = TogglePrevious,       // Ctrl+Alt+D
        };

        var result = HotkeyPlan.Reconcile(desired, previous, failed: new[] { HotkeyAction.Toggle });

        Assert.False(result.ContainsKey(HotkeyAction.Toggle));
        Assert.Equal(ClearAllDesired, result[HotkeyAction.ClearAll]);
    }

    [Fact]
    public void Multiple_failures_are_each_substituted_independently()
    {
        var desired = new Dictionary<HotkeyAction, HotkeySpec>
        {
            [HotkeyAction.Toggle] = ToggleDesired,
            [HotkeyAction.Brighter] = BrighterDesired,
            [HotkeyAction.Darker] = DarkerDesired,
        };
        var previous = new Dictionary<HotkeyAction, HotkeySpec>
        {
            [HotkeyAction.Toggle] = TogglePrevious,
            [HotkeyAction.Brighter] = new HotkeySpec(Keys.Control | Keys.Alt | Keys.B),
        };

        var result = HotkeyPlan.Reconcile(
            desired, previous, failed: new[] { HotkeyAction.Toggle, HotkeyAction.Brighter });

        Assert.Equal(TogglePrevious, result[HotkeyAction.Toggle]);
        Assert.Equal(new HotkeySpec(Keys.Control | Keys.Alt | Keys.B), result[HotkeyAction.Brighter]);
        Assert.Equal(DarkerDesired, result[HotkeyAction.Darker]);
    }

    [Fact]
    public void Empty_failed_collection_returns_desired_unchanged()
    {
        var desired = new Dictionary<HotkeyAction, HotkeySpec>
        {
            [HotkeyAction.Toggle] = ToggleDesired,
        };

        var result = HotkeyPlan.Reconcile(desired, previous: new Dictionary<HotkeyAction, HotkeySpec>(),
            failed: new List<HotkeyAction>());

        Assert.Equal(desired, result);
    }

    [Fact]
    public void Never_returns_an_invalid_spec()
    {
        var desired = new Dictionary<HotkeyAction, HotkeySpec>
        {
            [HotkeyAction.Toggle] = ToggleDesired,
        };
        var previous = new Dictionary<HotkeyAction, HotkeySpec>
        {
            [HotkeyAction.Toggle] = HotkeySpec.None,   // 손상되거나 비어 있는 이전 값
        };

        var result = HotkeyPlan.Reconcile(desired, previous, failed: new[] { HotkeyAction.Toggle });

        Assert.False(result.ContainsKey(HotkeyAction.Toggle));
    }
}
