namespace WinDimmer;

/// <summary>가림 토글 한 번이 세션에 해야 할 일 (설계 문서 §2의 표).</summary>
public enum BlackoutOp
{
    /// <summary>디밍 안 된 창 — 세션을 만들어 255로 가리고, 가림이 만든 세션임을 표시한다.</summary>
    StartNew,

    /// <summary>디밍 중인 창 — 현재 밝기를 기억하고 255로 덮는다.</summary>
    Cover,

    /// <summary>가림이 만든 세션 — 통째로 해제해 흔적을 없앤다.</summary>
    Release,

    /// <summary>원래 디밍 중이던 창 — 기억해 둔 밝기로 되돌린다.</summary>
    Restore,
}

/// <summary>
/// 가리기 직전 상태의 기억. 세션이 소유하며 세션과 함께 소멸한다.
/// <see cref="CreatedByBlackout"/>가 true면 가림 해제가 곧 세션 해제다 — 원래 디밍하지
/// 않던 창이므로 흔적 없이 원상복구해야 한다.
/// </summary>
public readonly record struct BlackoutMemory(byte PrevAlpha, bool PrevCustom, bool CreatedByBlackout);

/// <summary>완전 가림 토글의 상태 전이 판정. 순수 로직이라 그대로 테스트한다.</summary>
public static class BlackoutPlan
{
    /// <summary>가림 알파 — 완전 불투명 검정.</summary>
    public const byte CoverAlpha = byte.MaxValue;

    public static BlackoutOp Next(bool isDimmed, BlackoutMemory? memory)
    {
        if (!isDimmed) return BlackoutOp.StartNew;
        if (memory is null) return BlackoutOp.Cover;
        return memory.Value.CreatedByBlackout ? BlackoutOp.Release : BlackoutOp.Restore;
    }
}
