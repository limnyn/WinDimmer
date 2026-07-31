namespace WinDimmer;

/// <summary>
/// 단축키 등록 실패를 어떻게 수습할지 결정하는 순수 계산.
/// TrayApp에 섞여 있던 맵 연산을 떼어내 테스트 가능하게 만든 것이다.
/// </summary>
public static class HotkeyPlan
{
    /// <summary>
    /// 등록에 실패한 동작을 이전 조합으로 되돌린 재시도용 맵을 만든다.
    /// 되돌린 조합이 이번에 다른 동작에 배정된 조합과 겹치면 그 동작은 제외한다 —
    /// 같은 조합을 두 id로 등록할 수는 없기 때문이다.
    /// </summary>
    public static IReadOnlyDictionary<HotkeyAction, HotkeySpec> Reconcile(
        IReadOnlyDictionary<HotkeyAction, HotkeySpec> desired,
        IReadOnlyDictionary<HotkeyAction, HotkeySpec> previous,
        IReadOnlyCollection<HotkeyAction> failed)
    {
        var result = new Dictionary<HotkeyAction, HotkeySpec>(desired);

        // 실패한 동작은 이전 조합으로 되돌리되, 이전 조합조차 없으면(예: 시작 시점처럼
        // "이전"이라는 개념이 없는 경우) 아예 뺀다.
        var substituted = new HashSet<HotkeyAction>();
        foreach (HotkeyAction action in failed)
        {
            if (previous.TryGetValue(action, out HotkeySpec prevSpec))
            {
                result[action] = prevSpec;
                substituted.Add(action);
            }
            else
            {
                result.Remove(action);
            }
        }

        // 되돌린 값이 이번에 다른 동작이 새로 요청한 조합과 겹치면, 새 요청(desired) 쪽을
        // 남기고 되돌린(substituted) 쪽을 뺀다 — 되돌린 값이 새 요청을 밀어낼 수는 없다.
        foreach (IGrouping<HotkeySpec, KeyValuePair<HotkeyAction, HotkeySpec>> group in
            result.GroupBy(kv => kv.Value).Where(g => g.Count() > 1).ToList())
        {
            List<KeyValuePair<HotkeyAction, HotkeySpec>> members = group.ToList();
            bool hasDesiredMember = members.Any(kv => !substituted.Contains(kv.Key));

            IEnumerable<HotkeyAction> toRemove = hasDesiredMember
                ? members.Where(kv => substituted.Contains(kv.Key)).Select(kv => kv.Key)
                : members.Skip(1).Select(kv => kv.Key);   // 전부 되돌린 값끼리 겹치는 드문 경우 — 하나만 남긴다

            foreach (HotkeyAction action in toRemove)
                result.Remove(action);
        }

        foreach (HotkeyAction action in result.Keys.ToList())
        {
            if (!result[action].IsValid) result.Remove(action);
        }

        return result;
    }
}
