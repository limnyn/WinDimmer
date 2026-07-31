using System.Drawing;
using WinDimmer.Native;
using static WinDimmer.Native.Constants;

namespace WinDimmer;

/// <summary>
/// 레이어드·클릭통과 오버레이 창.
/// WinForms의 Location/Size 속성은 DPI 스케일링을 적용하므로 절대 쓰지 않는다.
/// 위치는 전부 SetWindowPos에 물리 픽셀을 직접 넘긴다.
/// </summary>
internal sealed class OverlayWindow : Form
{
    private byte _alpha = AlphaMath.Default;
    private Rect _lastBounds;
    private bool _shown;

    public OverlayWindow()
    {
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        StartPosition = FormStartPosition.Manual;
        Text = "WinDimmer Overlay";
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.Style = WS_POPUP;  // 기본 스타일을 대체 (WS_CLIPCHILDREN, WS_CLIPSIBLINGS 제거, 레이어드 창에는 무해)
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public void ApplyAlpha(byte alpha)
    {
        _alpha = alpha;
        if (IsHandleCreated)
            User32.SetLayeredWindowAttributes(Handle, 0, alpha, LWA_ALPHA);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        User32.SetLayeredWindowAttributes(Handle, 0, _alpha, LWA_ALPHA);
    }

    /// <summary>위치·크기·Z-order를 한 번의 SetWindowPos로 적용한다.</summary>
    public void MoveTo(Rect bounds, IntPtr insertAfter, bool changeZ)
    {
        // Control.Handle은 핸들이 없으면 새로 만든다. 오버레이 HWND가 이미 파괴됐는데
        // 아직 EVENT_OBJECT_DESTROY를 처리하기 전에 안전망 타이머가 여기 도달하면,
        // 핸들을 되살려 잠깐 검은 사각형이 깜빡이게 된다. 핸들이 없으면 아무 것도 하지 않는다.
        if (!IsHandleCreated)
        {
            DiagLog.Write("OverlayWindow.MoveTo skip reason=no-handle");
            return;
        }
        // 아무것도 하지 않는 경우는 기록하지 않는다. 안전망 타이머가 0.5초마다 여기를 지나므로
        // 남기면 로그가 그것만으로 가득 차 정작 봐야 할 Z-order 사건이 밀려 사라진다.
        if (!changeZ && bounds == _lastBounds) return;   // 변화 없으면 호출 자체를 생략

        uint flags = SWP_NOACTIVATE;
        if (!changeZ) flags |= SWP_NOZORDER;

        bool ok = User32.SetWindowPos(
            Handle,
            changeZ ? insertAfter : IntPtr.Zero,
            bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            flags);

        DiagLog.Write(
            $"OverlayWindow.MoveTo apply changeZ={changeZ} insertAfter=0x{insertAfter:X} " +
            $"bounds={bounds} result={ok}");

        if (ok)
        {
            _lastBounds = bounds;
        }
    }

    public void ShowNoActivate()
    {
        if (!IsHandleCreated)   // 파괴된 HWND를 되살리지 않는다
        {
            DiagLog.Write("OverlayWindow.ShowNoActivate skip reason=no-handle");
            return;
        }
        if (_shown) return;   // 같은 이유로 기록하지 않는다 — 타이머가 매번 지나간다
        bool ok = User32.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_SHOWWINDOW);
        DiagLog.Write($"OverlayWindow.ShowNoActivate apply result={ok}");
        if (ok)
        {
            _shown = true;
        }
    }

    public void HideOverlay()
    {
        if (!IsHandleCreated)   // 파괴된 HWND를 되살리지 않는다
        {
            DiagLog.Write("OverlayWindow.HideOverlay skip reason=no-handle");
            return;
        }
        if (!_shown)
        {
            DiagLog.Write("OverlayWindow.HideOverlay skip reason=already-hidden");
            return;
        }
        bool ok = User32.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_HIDEWINDOW);
        DiagLog.Write($"OverlayWindow.HideOverlay apply result={ok}");
        if (ok)
        {
            _shown = false;
        }
    }

    /// <summary>창 핸들을 미리 생성한다. 아직 없으면 생성, 있으면 아무것도 하지 않는다.</summary>
    public void EnsureHandle()
    {
        if (!IsHandleCreated) CreateHandle();
    }
}
