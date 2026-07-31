namespace WinDimmer;

/// <summary>
/// 사용자가 고른 창에서 규칙 초안을 만든다.
/// 제목 패턴을 비워 두는 것이 의도다 — 빈 패턴은 해당 프로세스의 모든 창에 일치하며,
/// 대부분의 사용자가 원하는 동작이다. 특정 제목만 걸려면 목록에서 직접 채운다.
/// </summary>
public static class RuleDraft
{
    /// <summary>프로세스명을 얻지 못했으면 null을 반환한다 (규칙을 만들 수 없다).</summary>
    public static DimRule? FromWindow(string processName, byte alpha)
    {
        if (string.IsNullOrWhiteSpace(processName)) return null;

        return new DimRule
        {
            ProcessName = processName.Trim(),
            TitlePattern = string.Empty,
            Alpha = alpha,
            Enabled = true,
        };
    }
}
