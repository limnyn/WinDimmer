using System.Windows.Forms;
using WinDimmer;
using Xunit;

public class HotkeySpecTests
{
    [Fact]
    public void Format_uses_ascii_and_fixed_modifier_order()
    {
        var spec = new HotkeySpec(Keys.Control | Keys.Alt | Keys.D);
        Assert.Equal("Ctrl+Alt+D", spec.Format());
    }

    [Fact]
    public void Format_writes_arrow_keys_as_ascii_names()
    {
        Assert.Equal("Ctrl+Alt+Up", new HotkeySpec(Keys.Control | Keys.Alt | Keys.Up).Format());
        Assert.Equal("Ctrl+Alt+Down", new HotkeySpec(Keys.Control | Keys.Alt | Keys.Down).Format());
    }

    [Fact]
    public void Display_writes_arrow_keys_as_symbols()
    {
        Assert.Equal("Ctrl+Alt+↑", new HotkeySpec(Keys.Control | Keys.Alt | Keys.Up).Display());
        Assert.Equal("Ctrl+Alt+↓", new HotkeySpec(Keys.Control | Keys.Alt | Keys.Down).Display());
    }

    [Fact]
    public void Display_matches_format_for_ordinary_keys()
    {
        var spec = new HotkeySpec(Keys.Control | Keys.Shift | Keys.X);
        Assert.Equal(spec.Format(), spec.Display());
    }

    [Fact]
    public void Modifier_order_is_fixed_regardless_of_flag_order()
    {
        var a = new HotkeySpec(Keys.Shift | Keys.Alt | Keys.Control | Keys.K);
        Assert.Equal("Ctrl+Alt+Shift+K", a.Format());
    }

    [Theory]
    [InlineData("Ctrl+Alt+D")]
    [InlineData("Ctrl+Alt+Up")]
    [InlineData("Ctrl+Shift+F5")]
    [InlineData("Ctrl+Alt+Shift+K")]
    public void TryParse_round_trips_format_output(string text)
    {
        Assert.True(HotkeySpec.TryParse(text, out HotkeySpec spec));
        Assert.Equal(text, spec.Format());
    }

    [Fact]
    public void TryParse_accepts_display_form_too()
    {
        Assert.True(HotkeySpec.TryParse("Ctrl+Alt+↑", out HotkeySpec spec));
        Assert.Equal("Ctrl+Alt+Up", spec.Format());
    }

    [Theory]
    [InlineData("Alt+Ctrl+D")]      // 순서가 뒤바뀜
    [InlineData(" Ctrl + Alt + D ")] // 공백
    [InlineData("ctrl+alt+d")]       // 대소문자
    public void TryParse_is_lenient_about_order_spacing_and_case(string text)
    {
        Assert.True(HotkeySpec.TryParse(text, out HotkeySpec spec));
        Assert.Equal("Ctrl+Alt+D", spec.Format());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("쓰레기")]
    [InlineData("Ctrl+Alt+NotAKey")]
    [InlineData("Ctrl+")]
    [InlineData("+D")]
    public void TryParse_rejects_garbage(string text)
    {
        Assert.False(HotkeySpec.TryParse(text, out _));
    }

    [Fact]
    public void A_key_without_a_modifier_is_invalid()
    {
        // 수정키가 없으면 그 키가 모든 앱에서 가로채인다
        Assert.False(new HotkeySpec(Keys.D).IsValid);
    }

    [Fact]
    public void Modifiers_without_a_key_are_invalid()
    {
        Assert.False(new HotkeySpec(Keys.Control | Keys.Alt).IsValid);
    }

    [Fact]
    public void None_is_invalid()
    {
        Assert.False(HotkeySpec.None.IsValid);
    }

    [Fact]
    public void A_modifier_plus_a_key_is_valid()
    {
        Assert.True(new HotkeySpec(Keys.Control | Keys.D).IsValid);
        Assert.True(new HotkeySpec(Keys.Alt | Keys.F1).IsValid);
        Assert.True(new HotkeySpec(Keys.Shift | Keys.Control | Keys.Up).IsValid);
    }

    [Fact]
    public void TryParse_rejects_a_combination_without_a_modifier()
    {
        Assert.False(HotkeySpec.TryParse("D", out _));
    }

    [Fact]
    public void ToWin32_maps_modifiers_and_virtual_key()
    {
        (uint mods, uint vk) = new HotkeySpec(Keys.Control | Keys.Alt | Keys.D).ToWin32();

        Assert.Equal(0x0002u | 0x0001u, mods);   // MOD_CONTROL | MOD_ALT
        Assert.Equal(0x44u, vk);                 // 'D'
    }

    [Fact]
    public void ToWin32_maps_shift_and_arrow_keys()
    {
        (uint mods, uint vk) = new HotkeySpec(Keys.Shift | Keys.Up).ToWin32();

        Assert.Equal(0x0004u, mods);   // MOD_SHIFT
        Assert.Equal(0x26u, vk);       // VK_UP
    }

    [Fact]
    public void A_key_code_that_is_itself_a_modifier_key_is_invalid()
    {
        // ControlKey는 수정키 비트 대신 실제 키 코드로 취급되며, 이는 "Ctrl을 누르고 있지만 실제 키를 안 눌렀다"는 상태다.
        Assert.False(new HotkeySpec(Keys.Control | Keys.ControlKey).IsValid);
        Assert.False(new HotkeySpec(Keys.Alt | Keys.Menu).IsValid);
        Assert.False(new HotkeySpec(Keys.Shift | Keys.ShiftKey).IsValid);
    }

    [Fact]
    public void Win_keys_are_rejected_as_key_code()
    {
        // OS 예약 단축키와 충돌하므로 지원하지 않는다.
        Assert.False(new HotkeySpec(Keys.Control | Keys.LWin).IsValid);
        Assert.False(new HotkeySpec(Keys.Control | Keys.RWin).IsValid);
    }

    [Fact]
    public void TryParse_rejects_win_key()
    {
        Assert.False(HotkeySpec.TryParse("Win+D", out _));
    }

    [Fact]
    public void TryParse_rejects_two_non_modifier_keys()
    {
        // 실제 키가 둘 이상이면 무효다.
        Assert.False(HotkeySpec.TryParse("Ctrl+A+B", out _));
    }

    [Fact]
    public void Display_renders_left_and_right_arrow_keys_as_symbols()
    {
        Assert.Equal("Ctrl+Alt+←", new HotkeySpec(Keys.Control | Keys.Alt | Keys.Left).Display());
        Assert.Equal("Ctrl+Alt+→", new HotkeySpec(Keys.Control | Keys.Alt | Keys.Right).Display());
    }

    [Fact]
    public void TryParse_accepts_duplicated_modifiers_and_normalizes_them()
    {
        // 수정키가 반복되면 멱등성에 따라 정규화된다.
        Assert.True(HotkeySpec.TryParse("Ctrl+Ctrl+D", out HotkeySpec spec));
        Assert.Equal("Ctrl+D", spec.Format());
    }

    [Fact]
    public void TryParse_rejects_a_purely_numeric_key_token()
    {
        // Enum.TryParse<Keys>("1", ...)는 Keys.LButton(마우스 왼쪽 버튼, 0x01)으로 파싱돼 버린다.
        Assert.False(HotkeySpec.TryParse("Ctrl+Alt+1", out _));
    }

    [Fact]
    public void TryParse_still_accepts_the_D_prefixed_digit_key()
    {
        Assert.True(HotkeySpec.TryParse("Ctrl+Alt+D1", out HotkeySpec spec));
        Assert.Equal("Ctrl+Alt+D1", spec.Format());
    }

    [Fact]
    public void HeldVirtualKeys_lists_every_modifier_and_the_key_itself()
    {
        Assert.True(HotkeySpec.TryParse("Ctrl+Alt+Up", out HotkeySpec spec));

        // 0x11 Ctrl, 0x12 Alt, 0x26 Up
        Assert.Equal(new[] { 0x11, 0x12, 0x26 }, spec.HeldVirtualKeys());
    }

    [Fact]
    public void HeldVirtualKeys_includes_shift_when_present()
    {
        Assert.True(HotkeySpec.TryParse("Ctrl+Shift+Down", out HotkeySpec spec));

        // 0x11 Ctrl, 0x10 Shift, 0x28 Down — 순서는 Ctrl → Alt → Shift → 실제 키로 고정된다.
        Assert.Equal(new[] { 0x11, 0x10, 0x28 }, spec.HeldVirtualKeys());
    }

    [Fact]
    public void HeldVirtualKeys_is_empty_for_an_invalid_combination()
    {
        // 빈 목록은 "판정 불가"를 뜻한다. 호출부(AlphaRamp)가 이걸 반복 금지로 다룬다 —
        // 빈 목록을 "전부 눌려 있음"으로 해석하면 램프가 영영 멈추지 않는다.
        Assert.Empty(HotkeySpec.None.HeldVirtualKeys());
        Assert.Empty(new HotkeySpec(System.Windows.Forms.Keys.Up).HeldVirtualKeys());
    }
}
