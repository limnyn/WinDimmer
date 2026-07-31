namespace WinDimmer;

/// <summary>Z-order 한 칸. 순수 판정 함수에 넘기기 위해 필요한 것만 담는다.</summary>
/// <param name="Handle">그 창.</param>
/// <param name="Owner">그 창의 소유자. 없으면 <see cref="IntPtr.Zero"/>.</param>
/// <param name="IsVisible">화면에 그려지는 창인지.</param>
public readonly record struct ZWindow(IntPtr Handle, IntPtr Owner, bool IsVisible);

/// <summary>오버레이를 Z-order 어디에 꽂을지 정한다.</summary>
public static class ZOrderPlan
{
    /// <summary>
    /// 오버레이를 **대상과 대상이 소유한 창들 전체** 바로 위에 놓기 위한 삽입 기준 창을 구한다.
    ///
    /// 대상 하나만 덮으면 안 되는 이유: 카카오톡의 광고 배너처럼, 겉보기에 창의 일부인 영역이
    /// 실제로는 대상이 소유한 별도의 최상위 창인 경우가 있다. 소유된 창은 언제나 소유자보다
    /// 위에 놓이므로, 오버레이를 대상 바로 위에만 두면 그 배너만 밝게 남아 창 안에 환한
    /// 직사각형이 생긴다. 그래서 소유된 창들까지 넘어간 지점에 꽂는다.
    ///
    /// 보이지 않는 창을 건너뛰는 이유: IME 헬퍼 같은 창이 사이에 끼는 일이 흔한데, 그리지 않는
    /// 창을 경계로 삼으면 오버레이가 필요 이상으로 아래에 남는다.
    /// </summary>
    /// <param name="above">
    /// 대상 바로 위부터 Z-order를 따라 위로 올라가며 나열한 창 목록. 가까운 것이 앞에 온다.
    /// </param>
    /// <param name="target">디밍 대상 창.</param>
    /// <param name="overlay">오버레이 자신. 목록에 들어 있어도 경계로 삼지 않는다.</param>
    /// <param name="reachedTop">
    /// <paramref name="above"/>가 Z-order 꼭대기까지 실제로 훑은 결과인지. 안전 상한에 걸려
    /// 도중에 끊긴 목록이면 false여야 한다 — 그 경우 "위에 아무것도 없다"는 결론을 내릴 수 없다.
    /// </param>
    /// <returns>
    /// 이 창 바로 뒤(아래)에 오버레이를 꽂으면 그룹 전체 위에 놓인다.
    /// <see cref="IntPtr.Zero"/>면 맨 위(HWND_TOP)로 올린다는 뜻이고,
    /// null이면 판단할 근거가 부족하므로 Z-order를 건드리지 말라는 뜻이다.
    /// </returns>
    public static IntPtr? InsertAfter(
        IReadOnlyList<ZWindow> above, IntPtr target, IntPtr overlay, bool reachedTop)
    {
        if (target == IntPtr.Zero) return null;

        foreach (ZWindow window in above)
        {
            if (IsBoundary(window, target, overlay)) return window.Handle;
        }

        // 그룹 밖의 창을 하나도 만나지 못했다. 꼭대기까지 훑은 결과라면 그룹이 맨 위라는 뜻이므로
        // HWND_TOP이 곧 "그룹 바로 위"다. 상한에 걸려 끊긴 목록이라면 알 수 없으므로 손대지 않는다.
        return reachedTop ? IntPtr.Zero : null;
    }

    /// <summary>
    /// 이 창에서 훑기를 멈춰야 하는지 — 곧 오버레이를 이 창 바로 아래에 꽂아야 하는지.
    ///
    /// 훑는 쪽(<c>RestackStrategy</c>)이 답이 나온 즉시 멈추려면 같은 판정이 필요하다. 판정을
    /// 양쪽에 각각 적으면 한쪽만 고쳐져 조용히 어긋나므로, 여기 하나만 두고 함께 쓴다.
    /// </summary>
    public static bool IsBoundary(ZWindow window, IntPtr target, IntPtr overlay) =>
        window.Handle != overlay &&      // 자기 자신은 경계가 아니다
        window.Owner != target &&        // 대상이 소유한 창 — 함께 덮어야 한다
        window.IsVisible;                // 그리지 않는 창은 경계가 아니다
}
