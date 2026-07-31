using System.Windows.Forms;
using WinDimmer.Native;

namespace WinDimmer;

/// <summary>
/// 전역 단축키 조합을 표현하는 순수 값 타입.
/// 저장 형식은 ASCII("Ctrl+Alt+Up"), 표시 형식은 방향키에 화살표 기호를 쓴다("Ctrl+Alt+↑").
/// </summary>
public readonly record struct HotkeySpec(Keys Value)
{
    /// <summary>수정키도 키도 없는 빈 조합.</summary>
    public static readonly HotkeySpec None = new(Keys.None);

    /// <summary>
    /// 유효한 전역 단축키인지 검사한다.
    /// 수정키가 하나 이상 있어야 하고(없으면 그 키가 모든 앱에서 가로채인다),
    /// 실제 키가 있어야 하며, 그 키가 수정키 자체(Ctrl/Alt/Shift/Win)여서는 안 되고,
    /// Win 키 조합은 OS 예약 단축키와 충돌하므로 지원하지 않는다.
    /// </summary>
    public bool IsValid
    {
        get
        {
            Keys modifiers = Value & Keys.Modifiers;
            Keys keyCode = Value & Keys.KeyCode;

            if (modifiers == Keys.None)
                return false;
            if (keyCode == Keys.None)
                return false;
            if (IsModifierKeyCode(keyCode))
                return false;

            return true;
        }
    }

    private static bool IsModifierKeyCode(Keys keyCode) =>
        keyCode is Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin;

    /// <summary>ASCII 저장 형식으로 직렬화한다. 예: "Ctrl+Alt+D", "Ctrl+Alt+Up".</summary>
    public string Format() => Compose(useSymbols: false);

    /// <summary>사용자에게 보여줄 표시 형식. 방향키만 화살표 기호로 바뀐다.</summary>
    public string Display() => Compose(useSymbols: true);

    private string Compose(bool useSymbols)
    {
        Keys modifiers = Value & Keys.Modifiers;
        Keys keyCode = Value & Keys.KeyCode;

        var parts = new List<string>();
        // 수정키는 Ctrl → Alt → Shift 순서로 고정한다 (플래그 조합 순서와 무관하게).
        if (modifiers.HasFlag(Keys.Control))
            parts.Add("Ctrl");
        if (modifiers.HasFlag(Keys.Alt))
            parts.Add("Alt");
        if (modifiers.HasFlag(Keys.Shift))
            parts.Add("Shift");

        parts.Add(useSymbols ? KeyNameForDisplay(keyCode) : KeyNameForFormat(keyCode));

        return string.Join("+", parts);
    }

    private static string KeyNameForFormat(Keys keyCode) => keyCode.ToString();

    private static string KeyNameForDisplay(Keys keyCode) => keyCode switch
    {
        Keys.Up => "↑",
        Keys.Down => "↓",
        Keys.Left => "←",
        Keys.Right => "→",
        _ => keyCode.ToString(),
    };

    /// <summary>
    /// ASCII 저장 형식과 화살표 기호를 쓰는 표시 형식 모두를 파싱한다.
    /// 공백, 대소문자, 수정키 순서에 관대하다. 수정키가 없거나 실제 키가 둘 이상이면 실패한다.
    /// </summary>
    public static bool TryParse(string? text, out HotkeySpec spec)
    {
        spec = None;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        string[] rawParts = text.Split('+');
        // "+D"처럼 빈 조각(선행 구분자로 인한 것 등)이 섞이면 무효로 취급한다.
        var tokens = new List<string>(rawParts.Length);
        foreach (string raw in rawParts)
        {
            string token = raw.Trim();
            if (token.Length == 0)
                return false;
            tokens.Add(token);
        }

        if (tokens.Count == 0)
            return false;

        Keys modifiers = Keys.None;
        Keys? keyCode = null;

        foreach (string token in tokens)
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= Keys.Control;
                    continue;
                case "alt":
                    modifiers |= Keys.Alt;
                    continue;
                case "shift":
                    modifiers |= Keys.Shift;
                    continue;
            }

            // 숫자만으로 된 토큰은 거른다 — Enum.TryParse<Keys>는 "1"을 Keys.LButton(마우스 왼쪽 버튼,
            // 값 0x01)으로 파싱해 버린다. 숫자 키는 "D1"~"D9"/"D0"로 써야 한다.
            if (token.Length > 0 && token.All(char.IsDigit))
                return false;

            Keys? parsedKey = token switch
            {
                "↑" => Keys.Up,
                "↓" => Keys.Down,
                "←" => Keys.Left,
                "→" => Keys.Right,
                _ => Enum.TryParse<Keys>(token, ignoreCase: true, out Keys result) ? result : null,
            };

            if (parsedKey is null)
                return false;

            // 실제 키 조각이 이미 하나 있는데 또 나오면(Ctrl+A+B) 무효.
            if (keyCode is not null)
                return false;

            keyCode = parsedKey;
        }

        if (keyCode is null)
            return false;

        var candidate = new HotkeySpec(modifiers | keyCode.Value);
        if (!candidate.IsValid)
            return false;

        spec = candidate;
        return true;
    }

    /// <summary>
    /// 이 조합이 "지금 물리적으로 눌려 있는지" 판정하려면 **전부** 눌려 있어야 하는 가상 키 코드들.
    /// 수정키를 포함하는 이유: 밝기 램프 도중 사용자가 Ctrl만 떼고 방향키를 계속 누르고 있으면
    /// 그 조합은 더 이상 성립하지 않으므로 램프도 멈춰야 한다.
    /// 무효한 조합(수정키가 없거나 실제 키가 없는 경우)은 빈 목록을 반환한다 — "아무것도 안 눌려도
    /// 조건을 만족한다"로 해석되면 램프가 영영 멈추지 않으므로, 호출부는 빈 목록을 실패로 다뤄야 한다.
    /// </summary>
    public IReadOnlyList<int> HeldVirtualKeys()
    {
        if (!IsValid) return Array.Empty<int>();

        Keys modifiers = Value & Keys.Modifiers;

        var keys = new List<int>(4);
        if (modifiers.HasFlag(Keys.Control))
            keys.Add(Constants.VK_CONTROL);
        if (modifiers.HasFlag(Keys.Alt))
            keys.Add(Constants.VK_MENU);
        if (modifiers.HasFlag(Keys.Shift))
            keys.Add(Constants.VK_SHIFT);

        keys.Add((int)(Value & Keys.KeyCode));
        return keys;
    }

    /// <summary>Win32 RegisterHotKey에 넘길 (수정키 비트, 가상 키 코드) 쌍으로 변환한다.</summary>
    public (uint Modifiers, uint VirtualKey) ToWin32()
    {
        Keys modifiers = Value & Keys.Modifiers;

        uint mods = 0;
        if (modifiers.HasFlag(Keys.Alt))
            mods |= Constants.MOD_ALT;
        if (modifiers.HasFlag(Keys.Control))
            mods |= Constants.MOD_CONTROL;
        if (modifiers.HasFlag(Keys.Shift))
            mods |= Constants.MOD_SHIFT;

        uint vk = (uint)(Value & Keys.KeyCode);

        return (mods, vk);
    }
}
