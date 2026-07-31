namespace WinDimmer;

/// <summary>
/// 사용자가 직접 디밍한 프로세스 이름 목록에 대한 순수 집합 연산.
/// 대소문자를 구분하지 않고, 중복 없이 유지한다. 입력 목록은 절대 변경하지 않고 새 목록을 돌려준다.
/// </summary>
public static class AutoDimList
{
    /// <summary>이름을 추가한다. 공백만 있는 이름은 무시하고, 이미 있으면(대소문자 무시) 그대로 둔다.</summary>
    public static IReadOnlyList<string> Add(IReadOnlyList<string> list, string processName)
    {
        string trimmed = processName.Trim();
        if (trimmed.Length == 0) return list.ToList();
        if (Contains(list, trimmed)) return list.ToList();

        var result = new List<string>(list) { trimmed };
        return result;
    }

    /// <summary>
    /// 이름을 제거한다(대소문자 무시, 앞뒤 공백 무시). 없으면 아무 일도 하지 않는다.
    /// config.json을 손으로 편집해 목록 항목 자체에 공백이 섞여 들어갔을 수 있으므로,
    /// 비교 대상인 processName뿐 아니라 목록 쪽 각 항목도 다듬은 뒤 비교한다.
    /// </summary>
    public static IReadOnlyList<string> Remove(IReadOnlyList<string> list, string processName)
    {
        string trimmed = processName.Trim();
        return list.Where(name => !string.Equals(name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// 목록에 이름이 있는지(대소문자 무시, 앞뒤 공백 무시) 검사한다.
    /// <see cref="Remove"/>와 마찬가지로 목록 항목 쪽 공백도 다듬은 뒤 비교한다.
    /// </summary>
    public static bool Contains(IReadOnlyList<string> list, string processName)
    {
        string trimmed = processName.Trim();
        return list.Any(name => string.Equals(name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
