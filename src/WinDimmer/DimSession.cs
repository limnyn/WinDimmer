using WinDimmer.Native;
using WinDimmer.ZOrder;
using static WinDimmer.Native.Constants;

namespace WinDimmer;

/// <summary>대상 창 하나를 어둡게 유지하는 데 필요한 모든 것.</summary>
internal sealed class DimSession : IDisposable
{
    /// <summary>
    /// 포그라운드 전환이 끝난 뒤 한 번 더 재삽입하기까지 기다리는 시간.
    /// 안전망 타이머(0.5초)만으로도 결국 복구되지만, 그동안 디밍이 풀린 채로 보인다.
    /// </summary>
    private const int SettleDelayMs = 60;

    private readonly OverlayWindow _overlay;
    private readonly TargetTracker _tracker;
    private readonly RestackStrategy _strategy = new();
    private readonly System.Windows.Forms.Timer _settle;
    private bool _disposed;

    public IntPtr Target { get; }
    public byte Alpha { get; private set; }

    /// <summary>
    /// 이 창의 밝기를 사용자가 개별적으로 지정했는지. true면 "기본 밝기" 슬라이더를 움직여도
    /// 이 창은 따라가지 않는다 — 창마다 맞춰 둔 값이 전역 조작 한 번에 지워지지 않게 하는 표시다.
    /// </summary>
    public bool AlphaIsCustom { get; private set; }

    /// <summary>
    /// 오버라이드(완전 가림·디밍 걷기) 중이면 현재 종류와 직전 상태의 기억, 아니면 null.
    /// 세션이 소유하므로 세션이 끝나면 기억도 함께 사라진다 — 별도 정리가 필요 없다.
    /// </summary>
    public OverrideState? Override { get; private set; }

    public bool IsElevatedTarget { get; }
    public bool HooksRegistered => _tracker.HooksRegistered;

    /// <summary>진단용. 오버레이 창 핸들.</summary>
    public IntPtr OverlayHandle => _overlay.Handle;

    /// <summary>진단용. 오버레이 바로 아래(Z-order상) 창 핸들을 그때그때 새로 읽는다.</summary>
    public IntPtr ZNeighbor => User32.GetWindow(_overlay.Handle, GW_HWNDNEXT);

    public event Action<DimSession>? Ended;

    public DimSession(IntPtr target, byte alpha)
    {
        Target = target;
        Alpha = alpha;
        IsElevatedTarget = WindowInspector.IsElevated(target);

        _overlay = new OverlayWindow();
        _overlay.EnsureHandle();
        _overlay.ApplyAlpha(alpha);

        DiagLog.Write($"DimSession.ctor target=0x{target:X} overlay=0x{_overlay.Handle:X}");

        _settle = new System.Windows.Forms.Timer { Interval = SettleDelayMs };
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();   // 일회성이다
            Sync("settle", zMayHaveChanged: true);
        };

        _tracker = new TargetTracker(target);
        _tracker.Changed += () => Sync("hook-change", zMayHaveChanged: false);
        _tracker.ForegroundChanged += OnForegroundChanged;
        _tracker.Destroyed += End;
    }

    /// <summary>구독자가 붙은 뒤에 호출한다. 생성자에서 하면 Ended를 놓친다.</summary>
    public void Start() => Sync("start", zMayHaveChanged: true);

    /// <param name="custom">
    /// 사용자가 이 창 하나를 겨냥해 바꿨으면 true(핫키·창별 슬라이더),
    /// 전역 기본 밝기가 흘러들어온 것이면 false.
    /// </param>
    public void SetAlpha(byte alpha, bool custom)
    {
        // 오버라이드 중에 다른 경로(밝기 핫키·창별 슬라이더)로 밝기를 바꾸면 기억은 버린다 —
        // 이 창은 이제 "가림/걷기 중"이 아니라 "사용자가 직접 조절한 창"이다. 다음 토글은
        // 이 시점의 밝기를 새로 기억한다.
        Override = null;

        Alpha = alpha;
        if (custom) AlphaIsCustom = true;
        _overlay.ApplyAlpha(alpha);
    }

    /// <summary>
    /// 오버라이드 진입. 직전 상태를 기억하고 극단 알파(가림 255 / 걷기 0)를 적용한다.
    /// 오버라이드는 개별지정 취급이다 — 기본 밝기 슬라이더가 끌어내리면 안 되기 때문이다.
    /// </summary>
    public void EnterOverride(OverrideKind kind, OverrideMemory memory)
    {
        Override = new OverrideState(kind, memory);
        Alpha = DimOverridePlan.AlphaFor(kind);
        AlphaIsCustom = true;
        _overlay.ApplyAlpha(Alpha);
    }

    /// <summary>가림↔걷기 전환. 기억은 그대로 두고 극단 알파만 바꾼다.</summary>
    public void SwitchOverride(OverrideKind kind)
    {
        if (Override is not OverrideState state) return;

        Override = state with { Kind = kind };
        Alpha = DimOverridePlan.AlphaFor(kind);
        _overlay.ApplyAlpha(Alpha);
    }

    /// <summary>오버라이드 해제. 기억해 둔 밝기와 개별지정 여부를 그대로 되돌린다.</summary>
    public void ExitOverride()
    {
        if (Override is not OverrideState state) return;

        Override = null;
        Alpha = state.Memory.PrevAlpha;
        // SetAlpha와 달리 개별지정 표시를 끌어내릴 수도 있어야 한다 — 오버라이드가 세워 둔
        // 표시를 원래(기본 밝기를 따르던 상태)로 되돌리는 경우다.
        AlphaIsCustom = state.Memory.PrevCustom;
        _overlay.ApplyAlpha(Alpha);
    }

    private void OnForegroundChanged()
    {
        if (_disposed) return;

        // 포그라운드가 바뀌면 Z-order도 바뀌었을 수 있다. 재삽입 위치는 Sync가 다시 계산한다.
        // 여기서 따로 기록하지 않는 이유: 포커스 전환 한 번에 세션 수만큼 불리는데, 실제로
        // 자리가 어긋났을 때만 Restack.walk가 남기므로 그쪽이 훨씬 읽을 만하다.
        Sync("foreground", zMayHaveChanged: true);

        // 이 콜백은 훅에서 동기적으로 불린다 — OS가 창을 끌어올리는 도중일 수 있고, 그러면 방금
        // 한 재삽입이 곧바로 덮어써진다. 전환이 가라앉은 뒤 한 번 더 확인한다.
        _settle.Stop();
        _settle.Start();
    }

    /// <summary>
    /// 어떤 이벤트가 오든 이 메서드를 호출한다.
    /// 상태를 처음부터 다시 계산하므로 훅 누락에 자동 복구된다.
    /// </summary>
    public void Sync(string reason, bool zMayHaveChanged)
    {
        if (_disposed) return;

        DesiredState state = WindowGeometry.Compute(WindowInspector.Capture(Target));

        // 아무 일도 일어나지 않는 정기 통과는 기록하지 않는다 — 안전망 타이머는 0.5초마다,
        // settle은 포커스가 바뀔 때마다 세션 수만큼 지나간다. 실제로 재삽입이 일어나면
        // Restack.walk가 남기므로 잃는 정보도 없다. 남기면 로그가 그것만으로 가득 찬다.
        if (reason is not ("timer" or "settle") || state.Visibility != OverlayVisibility.Visible)
        {
            DiagLog.Write(
                $"DimSession.Sync reason={reason} visibility={state.Visibility} " +
                $"bounds={state.Bounds} zMayHaveChanged={zMayHaveChanged} target=0x{Target:X}");
        }

        switch (state.Visibility)
        {
            case OverlayVisibility.Destroy:
                End();
                break;
            case OverlayVisibility.Hidden:
                _overlay.HideOverlay();
                break;
            case OverlayVisibility.Visible:
                _strategy.OnSync(_overlay, Target, state.Bounds, zMayHaveChanged);
                _overlay.ShowNoActivate();
                break;
        }
    }

    private void End()
    {
        if (_disposed) return;
        Ended?.Invoke(this);
    }

    /// <summary>훅을 먼저 끊어야 오버레이를 파괴하는 동안 콜백이 재진입하지 않는다.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _tracker.Dispose();
        _settle.Stop();
        _settle.Dispose();
        _overlay.Dispose();
    }
}
