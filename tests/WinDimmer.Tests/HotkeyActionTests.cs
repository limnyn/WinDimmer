using WinDimmer;
using Xunit;

public class HotkeyActionTests
{
    [Fact]
    public void All_lists_every_action()
    {
        Assert.Equal(7, HotkeyActions.All.Count);
        Assert.Contains(HotkeyAction.Toggle, HotkeyActions.All);
        Assert.Contains(HotkeyAction.Brighter, HotkeyActions.All);
        Assert.Contains(HotkeyAction.Darker, HotkeyActions.All);
        Assert.Contains(HotkeyAction.Blackout, HotkeyActions.All);
        Assert.Contains(HotkeyAction.Lift, HotkeyActions.All);
        Assert.Contains(HotkeyAction.ClearAll, HotkeyActions.All);
        Assert.Contains(HotkeyAction.Pick, HotkeyActions.All);
    }

    [Fact]
    public void Every_default_is_a_valid_combination()
    {
        foreach (HotkeyAction action in HotkeyActions.All)
            Assert.True(HotkeyActions.Default(action).IsValid, $"{action} 기본값이 무효하다");
    }

    [Fact]
    public void Defaults_do_not_collide()
    {
        var seen = new HashSet<string>();
        foreach (HotkeyAction action in HotkeyActions.All)
            Assert.True(seen.Add(HotkeyActions.Default(action).Format()), $"{action} 기본값이 중복된다");
    }

    [Fact]
    public void Every_action_has_a_korean_display_name()
    {
        foreach (HotkeyAction action in HotkeyActions.All)
        {
            string name = HotkeyActions.DisplayName(action);
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.NotEqual(action.ToString(), name);   // 열거형 이름을 그대로 쓰지 않았는지
            // 한글 문자(한글음절 U+AC00–U+D7A3)가 최소 하나 포함되어야 한다.
            Assert.True(name.Any(c => c >= '가' && c <= '힣'), $"{action}의 표시명 '{name}'에 한글이 없다");
        }
    }

    [Fact]
    public void Defaults_map_contains_every_action()
    {
        var defaults = HotkeyActions.Defaults();
        Assert.Equal(HotkeyActions.All.Count, defaults.Count);
        foreach (HotkeyAction action in HotkeyActions.All)
            Assert.Equal(HotkeyActions.Default(action), defaults[action]);
    }

    [Fact]
    public void Brightness_defaults_are_the_arrow_keys_in_the_natural_direction()
    {
        // 위가 밝게, 아래가 어둡게 — 반대로 두면 어색하다는 것이 이 작업의 출발점이었다
        Assert.Equal("Ctrl+Alt+Up", HotkeyActions.Default(HotkeyAction.Brighter).Format());
        Assert.Equal("Ctrl+Alt+Down", HotkeyActions.Default(HotkeyAction.Darker).Format());
    }

    [Fact]
    public void Pick_default_is_ctrl_alt_t()
    {
        Assert.Equal("Ctrl+Alt+T", HotkeyActions.Default(HotkeyAction.Pick).Format());
    }

    [Fact]
    public void Pick_config_key_is_camel_case()
    {
        Assert.Equal("pick", HotkeyActions.ConfigKey(HotkeyAction.Pick));
    }

    [Fact]
    public void Blackout_default_is_ctrl_alt_right()
    {
        // 밝기 조절(↑/↓)과 같은 화살표 무리 — 오른쪽은 "덮는다"
        Assert.Equal("Ctrl+Alt+Right", HotkeyActions.Default(HotkeyAction.Blackout).Format());
    }

    [Fact]
    public void Lift_default_is_ctrl_alt_left()
    {
        // 왼쪽은 "걷는다" — 가림(→)의 반대 방향
        Assert.Equal("Ctrl+Alt+Left", HotkeyActions.Default(HotkeyAction.Lift).Format());
    }

    [Fact]
    public void Blackout_config_key_is_camel_case()
    {
        Assert.Equal("blackout", HotkeyActions.ConfigKey(HotkeyAction.Blackout));
    }

    [Fact]
    public void Lift_config_key_is_camel_case()
    {
        Assert.Equal("lift", HotkeyActions.ConfigKey(HotkeyAction.Lift));
    }
}
