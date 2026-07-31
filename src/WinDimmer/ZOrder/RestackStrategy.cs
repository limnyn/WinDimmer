using WinDimmer.Native;
using static WinDimmer.Native.Constants;

namespace WinDimmer.ZOrder;

/// <summary>
/// 이벤트가 왔을 때만 오버레이를 제자리로 재삽입한다. 위치와 Z-order가 SetWindowPos 한 번으로
/// 처리되므로 깜빡임이 적다. 이것은 폴링이 아니다 — 이벤트 기반 호출이다.
///
/// 주의: hWndInsertAfter는 "positioned window보다 Z-order상 앞(위)에 둘 창"을 뜻한다.
/// 즉 대상(target)을 그대로 넘기면 대상이 오버레이보다 위로 올라가 정반대 결과가 된다.
/// 오버레이를 무언가의 바로 위에 두려면 그보다 더 위에 있는 창을 넘겨야 한다.
///
/// "제자리"는 대상 바로 위가 아니라 **대상과 대상이 소유한 창들 전체의 바로 위**다.
/// 판정 자체는 순수 함수인 <see cref="ZOrderPlan"/>에 있고, 여기서는 Z-order를 훑어
/// 그 함수에 넘길 목록을 만드는 일만 한다.
/// </summary>
internal sealed class RestackStrategy
{
    /// <summary>손상된 Z-order 체인이 UI 스레드를 멈추지 않도록 위로 걷는 최대 칸 수.</summary>
    private const int MaxWalkSteps = 200;

    public void OnSync(OverlayWindow overlay, IntPtr target, Rect bounds, bool zMayHaveChanged)
    {
        if (!zMayHaveChanged)
        {
            // Z-order 변경이 필요 없는 이동 — 위치만 갱신한다.
            overlay.MoveTo(bounds, IntPtr.Zero, changeZ: false);
            return;
        }

        IntPtr? insertAfter = ComputeInsertAfter(overlay.Handle, target);
        if (insertAfter is null)
        {
            // 근거가 부족하다. 위치만 갱신하고 Z-order는 다음 이벤트에 다시 판단한다.
            overlay.MoveTo(bounds, IntPtr.Zero, changeZ: false);
            return;
        }

        // 이미 제자리면 건드리지 않는다. 안전망 타이머가 0.5초마다 여기를 지나므로, 확인 없이
        // 매번 SetWindowPos를 부르면 멀쩡한 창의 Z-order를 초당 두 번씩 흔들게 된다.
        if (User32.GetWindow(overlay.Handle, GW_HWNDPREV) == insertAfter.Value)
        {
            overlay.MoveTo(bounds, IntPtr.Zero, changeZ: false);
            return;
        }

        overlay.MoveTo(bounds, insertAfter.Value, changeZ: true);

        // 재삽입이 실제로 먹혔는지는 호출 직후의 이웃으로만 알 수 있다. SetWindowPos가 성공을
        // 반환해도 OS가 곧바로 다른 자리로 되돌리는 경우가 있어, 성공 여부만으로는 판단할 수 없다.
        DiagLog.Write(
            $"Restack.after insertAfter=0x{insertAfter.Value:X} " +
            $"overlayPrev=0x{User32.GetWindow(overlay.Handle, GW_HWNDPREV):X} " +
            $"overlayNext=0x{User32.GetWindow(overlay.Handle, GW_HWNDNEXT):X} " +
            $"target=0x{target:X}");
    }

    private static IntPtr? ComputeInsertAfter(IntPtr overlay, IntPtr target)
    {
        var above = new List<ZWindow>(MaxWalkSteps);
        IntPtr current = target;
        bool reachedTop = false;

        for (int i = 0; i < MaxWalkSteps; i++)
        {
            current = User32.GetWindow(current, GW_HWNDPREV);
            if (current == IntPtr.Zero)
            {
                reachedTop = true;
                break;
            }

            var window = new ZWindow(
                current,
                User32.GetWindow(current, GW_OWNER),
                User32.IsWindowVisible(current));
            above.Add(window);

            // 경계를 만났으면 답이 정해졌다. 나머지를 마저 훑을 이유가 없다 — 안전망 타이머가
            // 0.5초마다 세션마다 여기를 지나므로, 끝까지 훑으면 초당 수천 번의 호출이 된다.
            if (ZOrderPlan.IsBoundary(window, target, overlay)) break;
        }

        IntPtr? result = ZOrderPlan.InsertAfter(above, target, overlay, reachedTop);

        // 대상 위쪽 이웃 몇 칸을 그대로 남긴다. "왜 저 창을 기준으로 골랐는가"는 이것 없이는
        // 재구성할 수 없다 — 특히 광고 배너처럼 소유 관계가 걸린 창이 어디에 있었는지가 관건이다.
        // 이미 제자리인 통과(대부분이 그렇다)는 기록하지 않는다. 타이머가 0.5초마다 지나간다.
        if (result is null || User32.GetWindow(overlay, GW_HWNDPREV) != result.Value)
        {
            DiagLog.Write(
                $"Restack.walk target=0x{target:X} overlay=0x{overlay:X} reachedTop={reachedTop} " +
                $"result={(result is null ? "null" : $"0x{result.Value:X}")} above=[" +
                string.Join(" ", above.Take(6).Select(w =>
                    $"0x{w.Handle:X}(owner=0x{w.Owner:X},vis={w.IsVisible})")) +
                (above.Count > 6 ? $" +{above.Count - 6}개]" : "]"));
        }

        return result;
    }
}
