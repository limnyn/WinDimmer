using System.Diagnostics;
using System.Drawing;
using WinDimmer.Native;
using static WinDimmer.Native.Constants;

namespace WinDimmer;

/// <summary>
/// WH_MOUSE_LL로 클릭 한 번을 가로채 대상 창을 고른다.
/// 그 클릭은 대상에 전달하지 않고 삼킨다 — 그러지 않으면
/// 선택하는 순간 대상 앱의 버튼이 눌린다.
/// </summary>
internal sealed class WindowPicker : IDisposable
{
    private readonly User32.LowLevelHookProc _proc;   // GC 방지용 필드
    private readonly System.Windows.Forms.Timer _escapeTimer;
    private IntPtr _hook;
    private HintWindow? _hint;
    private HighlightFrame? _frame;
    private IntPtr _hovered;        // 직전에 강조한 창. 바뀔 때만 갱신한다
    private bool _disposed;

    public event Action<IntPtr>? Picked;
    public event Action? Cancelled;

    public bool IsActive => _hook != IntPtr.Zero;

    public WindowPicker()
    {
        _proc = OnMouse;
        _escapeTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _escapeTimer.Tick += (_, _) =>
        {
            if ((User32.GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0) Cancel();
        };
    }

    public void Start()
    {
        if (_disposed || IsActive) return;

        _hook = User32.SetWindowsHookExW(WH_MOUSE_LL, _proc, IntPtr.Zero, 0);
        DiagLog.Write($"WindowPicker.Start hookInstalled={_hook != IntPtr.Zero}");
        if (_hook == IntPtr.Zero)
        {
            Cancelled?.Invoke();
            return;
        }

        _hint = new HintWindow();
        _hint.ShowHint();
        _frame = new HighlightFrame();
        _escapeTimer.Start();
    }

    public void Cancel()
    {
        if (_disposed || !IsActive) return;
        Stop();
        Cancelled?.Invoke();
    }

    private void Stop()
    {
        DiagLog.Write($"WindowPicker.Stop hookWasInstalled={_hook != IntPtr.Zero}");
        _escapeTimer.Stop();
        if (_hook != IntPtr.Zero)
        {
            User32.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _hint?.Close();
        _hint?.Dispose();   // Close()의 암묵적 Dispose에 기대지 않는다 — 둘 다 idempotent라 안전하다
        _hint = null;
        _frame?.Close();
        _frame?.Dispose();
        _frame = null;
        _hovered = IntPtr.Zero;
    }

    private IntPtr OnMouse(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode < 0)
                return User32.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

            if (wParam == WM_MOUSEMOVE)
            {
                UpdateHover();
                return User32.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
            }

            if (wParam != WM_LBUTTONDOWN)
                return User32.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

            User32.GetCursorPos(out POINT p);
            IntPtr picked = ResolveTarget(p);

            Stop();

            // 훅 콜백은 빨리 반환해야 한다. 무거운 작업은 메시지 루프로 넘긴다.
            // 컨텍스트는 발화 시점에 읽는다 — 생성자 시점에는 아직 설치되지 않아 항상 null이다.
            SynchronizationContext? ctx = SynchronizationContext.Current;
            DiagLog.Write($"WindowPicker.OnMouse picked=0x{picked:X} postedToContext={ctx is not null}");
            if (ctx is not null) ctx.Post(_ => Picked?.Invoke(picked), null);
            else Picked?.Invoke(picked);   // 컨텍스트가 없으면 직접 호출한다. 이 시점에 훅은 이미 해제된 뒤다

            return 1;   // 클릭을 삼킨다
        }
        // 저수준 마우스 훅 콜백에서 예외가 새어나가면 프로세스가 죽으므로,
        // 예외를 반드시 삼켜야 한다. 다만 실제 결함을 감지할 수 없게 되므로
        // 진단 출력을 남긴다. 선택이 완료되기 전에 실패했다면 클릭을 삼키지 않고
        // 다음 훅으로 넘겨 사용자의 클릭이 사라지지 않게 한다.
        catch (Exception ex)
        {
            Trace.WriteLine($"WindowPicker.OnMouse에서 예외 발생 — {ex}");
            return User32.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }
    }

    /// <summary>
    /// 클릭(혹은 커서) 아래 창을 실제로 어둡게 할 창으로 해석한다.
    /// 최상위 창을 구한 뒤, 소유 창 사슬을 끝까지 따라 올라간다 —
    /// 광고 배너처럼 owner가 있는 보조 창을 클릭해도 그 owner(진짜 프로그램 창)를 고르게 된다.
    /// </summary>
    private static IntPtr ResolveTarget(POINT p)
    {
        IntPtr h = User32.GetAncestor(User32.WindowFromPoint(p), GA_ROOT);
        IntPtr owner;
        while (h != IntPtr.Zero && (owner = User32.GetWindow(h, GW_OWNER)) != IntPtr.Zero)
            h = owner;
        return h;
    }

    /// <summary>
    /// 커서 아래 창이 바뀌었을 때만 테두리와 안내를 갱신한다.
    /// 저수준 훅 콜백은 빨리 반환해야 한다 — 느리면 Windows가 훅을 강제 해제한다.
    /// 핸들 비교 한 번으로 대부분의 마우스 이동을 걸러낸다.
    /// </summary>
    private void UpdateHover()
    {
        User32.GetCursorPos(out POINT p);
        IntPtr target = ResolveTarget(p);

        if (target == _hovered) return;

        DesiredState state = WindowGeometry.Compute(WindowInspector.Capture(target));
        if (state.Visibility != OverlayVisibility.Visible)
        {
            _frame?.HideFrame();
            _hint?.SetTarget(string.Empty, string.Empty);
        }
        else
        {
            _frame?.ShowAt(state.Bounds);
            _hint?.SetTarget(WindowInspector.GetProcessName(target), WindowInspector.GetTitle(target));
        }

        // 두 경로 모두 정상 처리된 뒤에만 기록한다.
        // 예외가 나면 여기 도달하지 않으므로 다음 이동에서 다시 시도된다.
        _hovered = target;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _escapeTimer.Dispose();
    }

    /// <summary>
    /// 선택 모드 안내 창. 일회성 UI이므로 TopMost를 허용한다
    /// (오버레이 창에는 금지되지만 이 창은 오버레이가 아니다).
    /// </summary>
    private sealed class HintWindow : Form
    {
        // WinForms는 Control.Font를 자동으로 Dispose하지 않는다.
        // 인스턴스마다 새로 만들면 창 선택 모드에 진입할 때마다 GDI 오브젝트가 샌다.
        private static readonly Font HintFont = new("Segoe UI", 10f);

        private readonly Label _guide;
        private readonly Label _target;

        public HintWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(32, 32, 32);
            ForeColor = Color.White;
            StartPosition = FormStartPosition.Manual;
            Padding = new Padding(12, 8, 12, 8);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            _guide = new Label
            {
                AutoSize = true,
                Text = "클릭할 창을 선택하세요 · ESC 취소",
                Font = HintFont,
            };
            _target = new Label
            {
                AutoSize = true,
                Text = string.Empty,
                Font = HintFont,
            };

            var panel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
            };
            panel.Controls.Add(_guide);
            panel.Controls.Add(_target);

            Controls.Add(panel);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // 커서가 안내 창 위를 지날 때 자기 자신이 대상으로 잡히지 않게 한다
                cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        public void ShowHint()
        {
            User32.GetCursorPos(out POINT p);
            Show();
            Location = new Point(p.X + 16, p.Y + 16);
        }

        public void SetTarget(string processName, string title) =>
            _target.Text = HintText.Describe(processName, title);
    }
}
