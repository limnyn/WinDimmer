using System.Diagnostics;
using WinDimmer.Native;
using static WinDimmer.Native.Constants;

namespace WinDimmer;

/// <summary>
/// SetWinEventHook를 감싸 Win32 이벤트를 도메인 이벤트로 번역한다.
/// 콜백 델리게이트는 반드시 필드로 보관한다 — 지역 변수면 GC가 수거해 훅이 조용히 죽는다.
/// </summary>
internal sealed class TargetTracker : IDisposable
{
    private readonly User32.WinEventProc _proc;   // GC 방지용 필드
    private readonly List<IntPtr> _hooks = new();
    private readonly IntPtr _target;
    private bool _disposed;

    public event Action? Changed;
    public event Action? Destroyed;
    public event Action? ForegroundChanged;

    public bool HooksRegistered { get; }

    public TargetTracker(IntPtr target)
    {
        _target = target;
        _proc = OnWinEvent;

        uint threadId = User32.GetWindowThreadProcessId(target, out uint pid);

        // GetWindowThreadProcessId가 실패(핸들이 이미 무효)하면 pid가 0으로 남는데,
        // SetWinEventHook에서 idProcess == 0은 "모든 프로세스"를 의미한다.
        // 그대로 두면 프로세스 스코프 훅이 전역 훅이 되어 유휴 CPU 0% 요구를 깨뜨린다.
        bool pidValid = threadId != 0 && pid != 0;

        if (pidValid)
        {
            // 대상 프로세스로 범위를 좁힌 훅들.
            // 전역으로 걸면 모든 앱의 이벤트를 받아내 유휴 CPU 0% 요구를 못 지킨다.
            Register(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, pid, 0);
            Register(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND, pid, 0);
            Register(EVENT_OBJECT_DESTROY, EVENT_OBJECT_DESTROY, pid, 0);
        }

        // FOREGROUND는 '다른' 프로세스의 창이 앞으로 나오는 것을 알아야 하므로
        // 전역으로 걸 수밖에 없다. 창 전환 시에만 발생해 빈도가 낮다.
        // SKIPOWNPROCESS로 우리 자신의 창(트레이 메뉴 등) 전환은 걸러낸다.
        Register(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0, WINEVENT_SKIPOWNPROCESS);

        HooksRegistered = pidValid && _hooks.Count == 4;
    }

    private void Register(uint min, uint max, uint pid, uint extraFlags)
    {
        IntPtr h = User32.SetWinEventHook(
            min, max, IntPtr.Zero, _proc, pid, 0, WINEVENT_OUTOFCONTEXT | extraFlags);
        if (h != IntPtr.Zero) _hooks.Add(h);
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint thread, uint time)
    {
        if (_disposed) return;

        try
        {
            if (eventType == EVENT_SYSTEM_FOREGROUND)
            {
                ForegroundChanged?.Invoke();
                return;
            }

            // 자식 오브젝트 이벤트를 걸러낸다. 이걸 빼먹으면
            // 드래그 중 콜백이 수백 배로 늘어난다.
            if (idObject != OBJID_WINDOW || idChild != CHILDID_SELF) return;
            if (hwnd != _target) return;

            if (eventType == EVENT_OBJECT_DESTROY) Destroyed?.Invoke();
            else Changed?.Invoke();
        }
        // WinEvent 콜백에서 예외가 새어나가면 프로세스가 죽으므로,
        // 예외를 반드시 삼켜야 한다. 다만 실제 결함을 감지할 수 없게 되므로
        // 진단 출력을 남긴다.
        catch (Exception ex)
        {
            Trace.WriteLine($"TargetTracker.OnWinEvent에서 예외 발생 — {ex}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (IntPtr h in _hooks) User32.UnhookWinEvent(h);
        _hooks.Clear();
    }
}
