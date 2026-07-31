using System.Diagnostics;
using WinDimmer.Native;
using static WinDimmer.Native.Constants;

namespace WinDimmer;

/// <summary>
/// 전역 EVENT_OBJECT_SHOW 훅으로 새로 생긴 창에 규칙을 적용한다.
/// 이 이벤트는 매우 빈번하므로 싼 검사부터 순서대로 걸러야 한다.
/// </summary>
internal sealed class RuleWatcher : IDisposable
{
    private readonly User32.WinEventProc _proc;   // GC 방지용 필드
    private readonly IntPtr _hook;
    private RuleMatcher _matcher = new(Array.Empty<DimRule>());
    private IReadOnlyList<string> _autoDimProcesses = Array.Empty<string>();
    private bool _disposed;

    public event Action<IntPtr, DimRule>? RuleMatched;

    /// <summary>사용자가 직접 디밍했던 프로세스의 창이 (다시) 나타났을 때 발생한다.</summary>
    public event Action<IntPtr>? AutoDimMatched;

    public RuleWatcher()
    {
        _proc = OnWinEvent;
        _hook = User32.SetWinEventHook(
            EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _proc, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    public void UpdateRules(IEnumerable<DimRule> rules) => _matcher = new RuleMatcher(rules);

    /// <summary>자동 복원 대상 프로세스 이름 목록을 갱신한다.</summary>
    public void UpdateAutoDimProcesses(IReadOnlyList<string> processNames) => _autoDimProcesses = processNames;

    public IReadOnlyList<DimRule> InvalidRules => _matcher.InvalidRules;

    /// <summary>앱 시작 시 이미 열려 있는 창들에도 규칙을 적용한다.</summary>
    public void ApplyToExistingWindows()
    {
        User32.EnumWindows((hwnd, _) =>
        {
            // 열거 도중 창이 사라져도 Evaluate 내부의 각 P/Invoke 호출이
            // 실패값(빈 문자열 등)을 돌려줄 뿐이므로 예외로 열거가 끊기지 않는다.
            try { Evaluate(hwnd); }
            // EnumWindows 콜백에서 예외가 새어나가면 열거 자체가 중단되므로,
            // 예외를 반드시 삼켜야 한다. 다만 실제 결함을 감지할 수 없게 되므로
            // 진단 출력을 남긴다.
            catch (Exception ex)
            {
                Trace.WriteLine($"RuleWatcher: ApplyToExistingWindows 열거 중 예외 발생 — {ex}");
            }
            return true;
        }, IntPtr.Zero);
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint thread, uint time)
    {
        if (_disposed) return;

        try
        {
            // 1. 가장 싼 검사부터
            if (idObject != OBJID_WINDOW || idChild != CHILDID_SELF) return;
            // 2. 최상위 창만
            if (User32.GetAncestor(hwnd, GA_ROOT) != hwnd) return;

            Evaluate(hwnd);
        }
        // WinEvent 콜백에서 예외가 새어나가면 프로세스가 죽으므로,
        // 예외를 반드시 삼켜야 한다. 다만 실제 결함을 감지할 수 없게 되므로
        // 진단 출력을 남긴다.
        catch (Exception ex)
        {
            Trace.WriteLine($"RuleWatcher: OnWinEvent 콜백에서 예외 발생 — {ex}");
        }
    }

    private void Evaluate(IntPtr hwnd)
    {
        // 0. 규칙도 자동 복원 대상도 하나 없으면 이후의 비싼 조회 자체를 건너뛴다.
        if (!_matcher.HasRules && _autoDimProcesses.Count == 0) return;

        if (!User32.IsWindowVisible(hwnd)) return;

        // 2.5. 광고 배너·팝업 같은 보조 창은 자동 매칭 대상에서 제외한다.
        //      전부 GetWindowLong/GetWindow 수준의 싼 호출이므로 프로세스명 조회보다 먼저 확인한다.
        if (!AppWindowFilter.IsUserWindow(WindowInspector.GetKind(hwnd))) return;

        // 3. 프로세스명 조회 (비싸다)
        string process = WindowInspector.GetProcessName(hwnd);
        if (process.Length == 0) return;

        // 4. 마지막에 제목 정규식
        DimRule? rule = _matcher.Match(process, WindowInspector.GetTitle(hwnd));
        if (rule is not null)
        {
            RuleMatched?.Invoke(hwnd, rule);
            return;
        }

        // 5. 규칙에 없으면 사용자가 직접 디밍했던 프로세스인지 확인한다.
        if (AutoDimList.Contains(_autoDimProcesses, process)) AutoDimMatched?.Invoke(hwnd);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hook != IntPtr.Zero) User32.UnhookWinEvent(_hook);
    }
}
