namespace WinDimmer;

/// <summary>창 선택 안내에 표시할 문구를 만든다. 순수 함수라 단위 테스트가 된다.</summary>
public static class HintText
{
    public const int MaxTitleLength = 40;

    /// <summary>대상 창을 설명하는 한 줄. 프로세스명을 못 읽으면 그 사실을 알린다.</summary>
    public static string Describe(string processName, string title)
    {
        if (processName.Length == 0 && title.Length == 0)
            return "(선택할 수 없는 창)";

        if (processName.Length == 0)
        {
            // 프로세스 이름이 비어있으면 대개 권한 상승된 대상이다. 이 경우에도 창은 선택 가능하다
            return title.Length > 0
                ? $"(프로세스 이름을 읽을 수 없음 — 선택은 가능) — {Ellipsize(title, MaxTitleLength)}"
                : "(프로세스 이름을 읽을 수 없음 — 선택은 가능)";
        }

        return title.Length > 0
            ? $"{processName} — {Ellipsize(title, MaxTitleLength)}"
            : processName;
    }

    public static string Ellipsize(string text, int max)
    {
        if (text.Length <= max) return text;
        // max가 1 이하이면 말줄임표 한 글자조차 넣을 자리가 없다 (혹은 자리가 없다).
        // text[..(max - 1)]이 음수 인덱스가 되어 예외를 던지는 것을 막는다.
        if (max <= 1) return max <= 0 ? string.Empty : "…";
        return text[..(max - 1)] + "…";
    }
}
