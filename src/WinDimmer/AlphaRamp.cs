using WinDimmer.Native;

namespace WinDimmer;

/// <summary>
/// 밝기 단축키를 누르고 있는 동안 밝기가 계속 변하게 한다.
///
/// OS의 키 반복(<c>MOD_NOREPEAT</c>를 떼면 얻을 수 있다)을 쓰지 않고 직접 타이머를 도는 이유는
/// 두 가지다. 첫째, 반복 속도가 사용자의 윈도우 키보드 설정에 좌우되면 같은 프로그램이 PC마다
/// 다른 속도로 반응한다. 둘째, 기본 반복 속도(초당 약 30회)에 <see cref="AlphaMath.Step"/>을
/// 곱하면 1초도 안 돼 끝까지 튀어 조절이 불가능하다. 여기서는 모든 핫키가 <c>MOD_NOREPEAT</c>로
/// 등록된 채 한 번만 통지되고, 반복은 전적으로 이 타이머가 결정한다.
///
/// 키를 뗐는지는 <c>GetAsyncKeyState</c>로 확인한다. 전역 핫키에는 키 뗌 통지가 없기 때문이다.
/// 폴링이므로 마지막 한 단계가 <see cref="RepeatMs"/>만큼 늦게 멈출 수 있으나, 한 단계는
/// 사용자가 알아채지 못할 크기이므로 감수한다.
/// </summary>
internal sealed class AlphaRamp : IDisposable
{
    /// <summary>이 시간 안에 키를 떼면 한 단계만 적용된다 — 톡톡 누르는 조작을 방해하지 않는다.</summary>
    private const int InitialDelayMs = 400;

    /// <summary>반복 주기. Step 10 기준 0에서 255까지 약 2초가 걸린다.</summary>
    private const int RepeatMs = 80;

    private readonly System.Windows.Forms.Timer _timer;

    /// <summary>
    /// 한 단계 적용하고, 값이 실제로 바뀌었으면 true. 한계에 닿았거나 대상 창이 사라졌으면
    /// false를 돌려 램프를 끝낸다. 누를 때마다 <see cref="Begin"/>에서 새로 받는다 — 대상이
    /// 그때의 포그라운드 창으로 고정돼야, 램프 도중 포커스가 옮겨가도 처음 창만 계속 조절된다.
    /// </summary>
    private Func<int, bool>? _adjust;

    private IReadOnlyList<int> _held = Array.Empty<int>();
    private int _delta;
    private bool _disposed;

    public AlphaRamp()
    {
        _timer = new System.Windows.Forms.Timer();
        _timer.Tick += (_, _) => OnTick();
    }

    /// <summary>진단·시험용. 지금 반복 중인지.</summary>
    public bool IsRunning => _timer.Enabled;

    /// <summary>
    /// 한 단계를 즉시 적용하고, 키를 계속 누르고 있으면 이어서 반복하도록 램프를 건다.
    /// 이미 다른 방향으로 돌고 있었다면 그 램프를 대체한다.
    /// </summary>
    public void Begin(HotkeySpec spec, int delta, Func<int, bool> adjust)
    {
        Stop();
        _adjust = adjust;

        // 첫 단계는 램프 성립 여부와 무관하게 무조건 적용한다 — 한 번 누른 것은 언제나 한 단계다.
        bool changed = adjust(delta);
        if (!changed) return;

        IReadOnlyList<int> held = spec.HeldVirtualKeys();
        // 무효한 조합이면 "눌려 있는지" 판정 자체가 불가능하다. 반복하지 않고 한 단계로 끝낸다.
        if (held.Count == 0) return;

        _held = held;
        _delta = delta;
        _timer.Interval = InitialDelayMs;
        _timer.Start();
    }

    /// <summary>반복을 즉시 멈춘다. 돌고 있지 않아도 안전하다.</summary>
    public void Stop() => _timer.Stop();

    private void OnTick()
    {
        if (_adjust is null || !AllHeld())
        {
            Stop();
            return;
        }

        // 첫 틱까지는 InitialDelayMs, 그 뒤로는 RepeatMs. Interval 설정은 타이머를 재시작시키므로
        // 매 틱 대입해도 무방하지만, 값이 같을 때 굳이 재시작하지 않도록 한 번만 바꾼다.
        if (_timer.Interval != RepeatMs) _timer.Interval = RepeatMs;

        // 한계에 닿아 더 바뀌지 않으면 키를 누르고 있어도 계속 돌 이유가 없다.
        if (!_adjust(_delta)) Stop();
    }

    private bool AllHeld()
    {
        foreach (int vk in _held)
        {
            // 최상위 비트가 "지금 눌려 있음". 최하위 비트(마지막 조회 이후 눌린 적 있음)는 쓰지 않는다.
            if ((User32.GetAsyncKeyState(vk) & 0x8000) == 0) return false;
        }
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Stop();
        _timer.Dispose();
    }
}
