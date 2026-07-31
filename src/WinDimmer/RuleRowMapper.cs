namespace WinDimmer;

/// <summary>
/// 설정 창의 규칙 그리드 행을 <see cref="DimRule"/>로 매핑한다.
/// 순수 값(문자열/정수/bool)만 다루므로 단위 테스트가 된다.
/// </summary>
public static class RuleRowMapper
{
    /// <summary>
    /// 행 목록을 규칙 목록으로 변환한다.
    /// 프로세스명이 빈 행은 건너뛰고, 밝기는 0–255로 클램프하며,
    /// 순서는 입력 순서를 그대로 유지한다.
    /// 잘못된 제목 정규식을 만나면 그 즉시 실패로 보고하고 중단한다 —
    /// <see cref="RuleMatcher.IsValidPattern"/> 하나만 검증 경로로 쓴다.
    /// </summary>
    public static RuleRowMapResult Map(IEnumerable<RuleRowValues> rows)
    {
        var result = new List<DimRule>();

        foreach (RuleRowValues row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.ProcessName)) continue;

            if (row.TitlePattern.Length > 0 && !RuleMatcher.IsValidPattern(row.TitlePattern))
                return RuleRowMapResult.Failure(row.TitlePattern);

            result.Add(new DimRule
            {
                ProcessName = row.ProcessName.Trim(),
                TitlePattern = row.TitlePattern,
                Alpha = (byte)Math.Clamp(row.Alpha, 0, 255),
                Enabled = row.Enabled,
            });
        }

        return RuleRowMapResult.Success(result);
    }
}

/// <summary>그리드 행 하나의 값. <see cref="SettingsForm.RuleRow"/>와 별개로 둔 순수 입력 타입.</summary>
public readonly record struct RuleRowValues(string ProcessName, string TitlePattern, int Alpha, bool Enabled);

/// <summary>매핑 결과. 실패 시 문제가 된 정규식을 담는다.</summary>
public sealed class RuleRowMapResult
{
    public bool IsSuccess { get; }
    public List<DimRule> Rules { get; }
    public string? InvalidPattern { get; }

    private RuleRowMapResult(bool isSuccess, List<DimRule> rules, string? invalidPattern)
    {
        IsSuccess = isSuccess;
        Rules = rules;
        InvalidPattern = invalidPattern;
    }

    public static RuleRowMapResult Success(List<DimRule> rules) => new(true, rules, null);

    public static RuleRowMapResult Failure(string invalidPattern) => new(false, new List<DimRule>(), invalidPattern);
}
