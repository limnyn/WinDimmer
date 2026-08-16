using System.Windows.Forms;

namespace WinDimmer;

/// <summary>전역 단축키로 실행할 수 있는 동작.</summary>
public enum HotkeyAction
{
    /// <summary>대상 창 디밍 토글.</summary>
    Toggle,

    /// <summary>대상 창을 더 밝게.</summary>
    Brighter,

    /// <summary>대상 창을 더 어둡게.</summary>
    Darker,

    /// <summary>대상 창 완전 가림 토글 — 255로 덮거나 직전 상태로 복귀.</summary>
    Blackout,

    /// <summary>대상 창 디밍 걷기 토글 — 필터를 걷어(0) 원본을 보거나 직전 상태로 복귀.</summary>
    Lift,

    /// <summary>모든 디밍 해제.</summary>
    ClearAll,

    /// <summary>창 선택 모드 시작. 이미 디밍 중인 창을 고르면 그 창만 해제된다.</summary>
    Pick,
}

/// <summary>HotkeyAction에 대한 표시 이름과 기본 단축키 조합을 제공한다.</summary>
public static class HotkeyActions
{
    /// <summary>정의된 모든 동작.</summary>
    public static IReadOnlyList<HotkeyAction> All { get; } = Array.AsReadOnly(new[]
    {
        HotkeyAction.Toggle,
        HotkeyAction.Brighter,
        HotkeyAction.Darker,
        HotkeyAction.Blackout,
        HotkeyAction.Lift,
        HotkeyAction.ClearAll,
        HotkeyAction.Pick,
    });

    /// <summary>동작에 대한 한국어 표시 이름을 반환한다.</summary>
    public static string DisplayName(HotkeyAction action) => action switch
    {
        HotkeyAction.Toggle => "창 디밍 토글",
        HotkeyAction.Brighter => "밝게",
        HotkeyAction.Darker => "어둡게",
        HotkeyAction.Blackout => "완전 가림 토글",
        HotkeyAction.Lift => "디밍 걷기 토글",
        HotkeyAction.ClearAll => "전체 해제",
        HotkeyAction.Pick => "창 선택",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    /// <summary>동작에 대한 기본 단축키 조합을 반환한다.</summary>
    public static HotkeySpec Default(HotkeyAction action) => action switch
    {
        // 위가 밝게, 아래가 어둡게 — 자연스러운 방향.
        HotkeyAction.Toggle => new HotkeySpec(Keys.Control | Keys.Alt | Keys.D),
        HotkeyAction.Brighter => new HotkeySpec(Keys.Control | Keys.Alt | Keys.Up),
        HotkeyAction.Darker => new HotkeySpec(Keys.Control | Keys.Alt | Keys.Down),
        // 밝기 조절(↑/↓)과 같은 화살표 무리 — 오른쪽은 "덮고", 왼쪽은 "걷는다".
        HotkeyAction.Blackout => new HotkeySpec(Keys.Control | Keys.Alt | Keys.Right),
        HotkeyAction.Lift => new HotkeySpec(Keys.Control | Keys.Alt | Keys.Left),
        HotkeyAction.ClearAll => new HotkeySpec(Keys.Control | Keys.Alt | Keys.X),
        HotkeyAction.Pick => new HotkeySpec(Keys.Control | Keys.Alt | Keys.T),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    /// <summary>모든 동작에 대한 기본 단축키 조합의 사전을 반환한다.</summary>
    public static IReadOnlyDictionary<HotkeyAction, HotkeySpec> Defaults()
    {
        var result = new Dictionary<HotkeyAction, HotkeySpec>(All.Count);
        foreach (HotkeyAction action in All)
            result[action] = Default(action);
        return result;
    }

    /// <summary>
    /// config.json에 쓰는 camelCase 키. 예: Toggle → "toggle", ClearAll → "clearAll".
    /// 설계 문서(§4)가 약속한 형식이며, 사용자가 파일을 손으로 편집할 때 보게 되는 이름이다.
    /// </summary>
    public static string ConfigKey(HotkeyAction action)
    {
        string name = action.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
