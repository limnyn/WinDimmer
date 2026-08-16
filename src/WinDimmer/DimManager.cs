namespace WinDimmer;

/// <summary>
/// 세션 딕셔너리와 안전망 타이머를 관리한다.
/// UI는 세션을 하나만 만들도록 제한하지만 자료구조는 다중을 수용한다.
/// </summary>
internal sealed class DimManager : IDisposable
{
    private const int NormalIntervalMs = 500;
    private const int ElevatedIntervalMs = 100;

    private readonly Dictionary<IntPtr, DimSession> _sessions = new();
    private readonly System.Windows.Forms.Timer _safetyNet;
    private bool _disposed;

    /// <summary>세션이 생기거나 사라질 때 발생한다 (UI 갱신용).</summary>
    public event Action? SessionsChanged;

    /// <summary>상승 권한 대상이 잡혔을 때 한 번 발생한다 (안내 표시용).</summary>
    public event Action<DimSession>? ElevatedTargetDetected;

    public DimManager()
    {
        _safetyNet = new System.Windows.Forms.Timer { Interval = NormalIntervalMs };
        _safetyNet.Tick += (_, _) => OnSafetyNetTick();
    }

    // 스냅샷을 반환한다. 라이브 뷰를 그대로 넘기면 순회 중 세션이 사라질 때
    // (대상 창 파괴 콜백이 같은 스레드에서 Undim을 호출) InvalidOperationException이 난다.
    public IReadOnlyCollection<DimSession> Sessions => _sessions.Values.ToArray();
    public int Count => _sessions.Count;
    public bool IsDimmed(IntPtr target) => _sessions.ContainsKey(target);

    public bool TryDim(IntPtr target, byte alpha)
    {
        if (target == IntPtr.Zero || _sessions.ContainsKey(target))
        {
            DiagLog.Write(
                $"DimManager.TryDim reject target=0x{target:X} reason=" +
                (target == IntPtr.Zero ? "zero" : "already-dimmed"));
            return false;
        }

        // 앱 자신의 창(설정 창 등)은 절대 디밍 대상이 될 수 없다.
        // 그러지 않으면 설정 모달이 포커스된 상태에서 Ctrl+Alt+D를 누르는 것만으로
        // 자기 자신의 UI가 클릭 통과 검은 오버레이로 덮인다.
        if (WindowInspector.GetProcessId(target) == (uint)Environment.ProcessId)
        {
            DiagLog.Write($"DimManager.TryDim reject target=0x{target:X} reason=own-process");
            return false;
        }

        DesiredState state = WindowGeometry.Compute(WindowInspector.Capture(target));
        if (state.Visibility == OverlayVisibility.Destroy)
        {
            DiagLog.Write($"DimManager.TryDim reject target=0x{target:X} reason=not-visible");
            return false;
        }

        DiagLog.Write($"DimManager.TryDim accept target=0x{target:X}");

        var session = new DimSession(target, alpha);
        session.Ended += OnSessionEnded;
        _sessions[target] = session;

        // 구독과 등록이 끝난 뒤에 첫 동기화를 한다. 그 사이 대상이 죽었으면
        // Sync가 End()를 호출해 Ended가 발생하고, OnSessionEnded가 세션을 이미 제거한다.
        session.Start();
        if (!_sessions.ContainsKey(target)) return false;

        UpdateTimer();
        SessionsChanged?.Invoke();

        if (session.IsElevatedTarget) ElevatedTargetDetected?.Invoke(session);
        return true;
    }

    public bool Undim(IntPtr target)
    {
        if (!_sessions.Remove(target, out DimSession? session)) return false;
        session.Ended -= OnSessionEnded;
        session.Dispose();
        UpdateTimer();
        SessionsChanged?.Invoke();
        return true;
    }

    public void Toggle(IntPtr target, byte alpha)
    {
        if (IsDimmed(target)) Undim(target);
        else TryDim(target, alpha);
    }

    /// <summary>
    /// 완전 가림 토글 (설계 문서 2026-08-16 §2). 디밍 안 된 창은 세션을 만들어 255로 가리고,
    /// 가림 중이면 직전 상태로 되돌린다 — 가림이 만든 세션은 통째로 해제해 흔적을 없앤다.
    /// 자동 복원 목록에는 관여하지 않는다: 가림은 재시작하면 사라지는 휘발성 상태다.
    /// </summary>
    public bool ToggleBlackout(IntPtr target)
    {
        DimSession? session = Find(target);
        switch (BlackoutPlan.Next(session is not null, session?.Blackout))
        {
            case BlackoutOp.StartNew:
                if (!TryDim(target, BlackoutPlan.CoverAlpha)) return false;
                // TryDim이 true를 돌려도 시작 도중 대상이 죽어 세션이 이미 사라졌을 수 있다 —
                // Find로 다시 확인한다. PrevAlpha/PrevCustom은 Release 경로에서 쓰지 않는다.
                Find(target)?.EnterBlackout(new BlackoutMemory(0, false, CreatedByBlackout: true));
                return true;
            case BlackoutOp.Cover:
                session!.EnterBlackout(new BlackoutMemory(session.Alpha, session.AlphaIsCustom, CreatedByBlackout: false));
                return true;
            case BlackoutOp.Release:
                return Undim(target);
            case BlackoutOp.Restore:
                session!.ExitBlackout();
                return true;
            default:
                return false;
        }
    }

    /// <summary>디밍 중인 창의 세션을 찾는다. 디밍 중이 아니면 null.</summary>
    public DimSession? Find(IntPtr target) =>
        _sessions.TryGetValue(target, out DimSession? session) ? session : null;

    /// <summary>
    /// 창 하나의 밝기를 바꾸고 그 창을 "개별 지정됨"으로 표시한다.
    /// 대상이 디밍 중이 아니면 아무 일도 하지 않고 false를 반환한다.
    /// </summary>
    public bool SetAlpha(IntPtr target, byte alpha)
    {
        if (!_sessions.TryGetValue(target, out DimSession? session)) return false;

        session.SetAlpha(alpha, custom: true);
        return true;
    }

    /// <summary>
    /// 전역 기본 밝기를 적용한다. 사용자가 개별적으로 밝기를 지정한 창은 건너뛴다 —
    /// 그러지 않으면 창마다 맞춰 둔 값이 기본 밝기 슬라이더 한 번에 전부 지워진다.
    /// </summary>
    public void ApplyDefaultAlpha(byte alpha)
    {
        foreach (DimSession s in _sessions.Values.ToArray())
        {
            if (s.AlphaIsCustom) continue;
            s.SetAlpha(alpha, custom: false);
        }
    }

    /// <summary>디밍 중인 창을 전부 해제한다. 포커스와 무관하게 동작한다.</summary>
    public void UndimAll()
    {
        // 순회 중 Undim이 딕셔너리를 수정하므로 스냅샷을 돈다
        foreach (DimSession s in _sessions.Values.ToArray()) Undim(s.Target);
    }

    private void OnSessionEnded(DimSession session) => Undim(session.Target);

    private void OnSafetyNetTick()
    {
        // 순회 중 세션이 제거될 수 있으므로 복사본을 돈다.
        //
        // zMayHaveChanged를 true로 주는 것이 핵심이다. 포커스 전환 때 우리 재삽입은 OS가 창을
        // 끌어올리는 도중에 실행돼 곧바로 덮어써질 수 있는데, 여기서 Z-order를 다시 보지 않으면
        // 그 상태가 다음 이벤트가 올 때까지 굳는다("포커스를 주면 디밍이 풀린다"의 정체다).
        // 재삽입 자체는 이미 제자리일 때 건너뛰므로, 매번 확인해도 SetWindowPos가 늘어나지 않는다.
        foreach (DimSession s in _sessions.Values.ToArray())
            s.Sync("timer", zMayHaveChanged: true);
    }

    private void UpdateTimer()
    {
        if (_sessions.Count == 0)
        {
            _safetyNet.Stop();
            return;
        }

        // 상승 권한 대상은 훅 콜백이 오지 않는다. 세션이 하나라도 있으면
        // 앱 전체에 하나뿐인 이 타이머의 폴링 주기를 전부 올린다 (개별 세션 한정이 아니다).
        bool anyElevated = _sessions.Values.Any(s => s.IsElevatedTarget);
        _safetyNet.Interval = anyElevated ? ElevatedIntervalMs : NormalIntervalMs;
        _safetyNet.Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _safetyNet.Stop();
        _safetyNet.Dispose();
        foreach (DimSession s in _sessions.Values)
        {
            s.Ended -= OnSessionEnded;
            s.Dispose();
        }
        _sessions.Clear();
    }
}
