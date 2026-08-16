namespace WinDimmer;

/// <summary>오버라이드의 방향 — 완전 가림(255) 또는 디밍 걷기(0).</summary>
public enum OverrideKind
{
    /// <summary>완전 가림. 알파 255로 덮는다.</summary>
    Cover,

    /// <summary>디밍 걷기. 알파 0으로 걷어 원본을 보인다.</summary>
    Lift,
}

/// <summary>
/// 오버라이드 직전 상태의 기억. 세션이 소유하며 세션과 함께 소멸한다.
/// <see cref="CreatedByOverride"/>가 true면 오버라이드 해제가 곧 세션 해제다 — 원래
/// 디밍하지 않던 창이므로 흔적 없이 원상복구해야 한다.
/// </summary>
public readonly record struct OverrideMemory(byte PrevAlpha, bool PrevCustom, bool CreatedByOverride);

/// <summary>세션이 들고 있는 현재 오버라이드: 어느 극단에 있는지 + 직전 상태의 기억.</summary>
public readonly record struct OverrideState(OverrideKind Kind, OverrideMemory Memory);

/// <summary>
/// 가림(Ctrl+Alt+→)·걷기(Ctrl+Alt+←) 토글의 상태 전이 판정 (설계 문서 §2·부록 A).
/// 순수 로직이라 그대로 테스트한다.
/// </summary>
public static class DimOverridePlan
{
    /// <summary>가림 알파 — 완전 불투명 검정.</summary>
    public const byte CoverAlpha = byte.MaxValue;

    /// <summary>걷기 알파 — 완전 투명.</summary>
    public const byte LiftAlpha = 0;

    public static byte AlphaFor(OverrideKind kind) => kind == OverrideKind.Cover ? CoverAlpha : LiftAlpha;

    public static OverrideOp Next(OverrideKind pressed, bool isDimmed, OverrideState? current)
    {
        // 걷기는 걷을 필터가 있어야 의미가 있다 — 미디밍 창은 가림만 세션을 만든다.
        if (!isDimmed) return pressed == OverrideKind.Cover ? OverrideOp.Start : OverrideOp.None;

        if (current is not OverrideState state) return OverrideOp.Enter;

        // 오버라이드가 만든 세션의 "원본"은 미디밍 상태다 — 어느 키를 눌러도 해제가 곧 복원이다.
        if (state.Memory.CreatedByOverride) return OverrideOp.Release;

        // 같은 키는 복원, 반대 키는 반대 극단으로 전환(기억 유지) — 가림 중 급히 봐야 하거나
        // 걷은 중 급히 가려야 할 때 두 번 누르게 하지 않는다.
        return state.Kind == pressed ? OverrideOp.Restore : OverrideOp.Switch;
    }
}

/// <summary>토글 한 번이 세션에 해야 할 일 (설계 문서 부록 A의 전이 표).</summary>
public enum OverrideOp
{
    /// <summary>아무것도 하지 않는다 — 미디밍 창의 걷기.</summary>
    None,

    /// <summary>세션을 만들어 255로 가리고, 오버라이드가 만든 세션임을 표시한다.</summary>
    Start,

    /// <summary>현재 밝기를 기억하고 눌린 방향의 극단으로 간다.</summary>
    Enter,

    /// <summary>기억은 그대로 두고 반대 극단으로 전환한다.</summary>
    Switch,

    /// <summary>기억해 둔 밝기로 되돌린다.</summary>
    Restore,

    /// <summary>세션을 통째로 해제해 흔적을 없앤다.</summary>
    Release,
}
