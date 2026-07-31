using System.Text.RegularExpressions;

namespace WinDimmer;

/// <summary>
/// 프로세스명 + 제목 정규식으로 규칙을 찾는다.
/// 정규식에는 반드시 타임아웃을 준다 — 파국적 백트래킹은 UI 스레드를 통째로 멈춘다.
/// </summary>
public sealed class RuleMatcher
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private readonly List<(DimRule Rule, Regex? Pattern)> _compiled = new();
    private readonly List<DimRule> _invalid = new();

    public RuleMatcher(IEnumerable<DimRule> rules)
    {
        foreach (DimRule rule in rules)
        {
            if (!rule.Enabled) continue;

            if (rule.TitlePattern.Length == 0)
            {
                _compiled.Add((rule, null));   // 빈 패턴 = 모든 제목 허용
                continue;
            }

            try
            {
                _compiled.Add((rule, new Regex(rule.TitlePattern, RegexOptions.None, RegexTimeout)));
            }
            catch (ArgumentException)
            {
                _invalid.Add(rule);   // 잘못된 정규식은 비활성 처리하고 알린다. 앱은 죽이지 않는다.
            }
        }
    }

    public IReadOnlyList<DimRule> InvalidRules => _invalid;

    /// <summary>매칭 가능한 규칙이 하나도 없으면 비싼 조회 자체를 건너뛴다.</summary>
    public bool HasRules => _compiled.Count > 0;

    public DimRule? Match(string processName, string title)
    {
        foreach ((DimRule rule, Regex? pattern) in _compiled)
        {
            if (!string.Equals(rule.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (pattern is null) return rule;

            try
            {
                if (pattern.IsMatch(title)) return rule;
            }
            catch (RegexMatchTimeoutException)
            {
                // 타임아웃된 패턴은 이번 매칭에서 실패로 취급하고 계속 진행한다.
            }
        }

        return null;
    }

    /// <summary>정규식이 컴파일 가능한지 검사한다. 타임아웃 값을 한 곳에서만 관리하기 위해 여기 둔다.</summary>
    public static bool IsValidPattern(string pattern)
    {
        if (pattern.Length == 0) return true;   // 빈 패턴 = 모든 제목 허용

        try
        {
            _ = new Regex(pattern, RegexOptions.None, RegexTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
