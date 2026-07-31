using System.Drawing;
using System.Drawing.Drawing2D;
using WinDimmer.Native;
using static WinDimmer.Native.Constants;

namespace WinDimmer;

/// <summary>
/// 커서 아래 창의 경계를 표시하는 테두리 창.
/// 가운데를 도려낸 Region을 써서 테두리만 남기고, 클릭과 히트 테스트를 통과시킨다.
/// TopMost를 쓰지만 이것은 오버레이가 아니라 선택 모드에서만 존재하는 일회성 UI다.
/// </summary>
internal sealed class HighlightFrame : Form
{
    private const int Thickness = 4;

    private Rect _bounds;
    private bool _shown;   // Visible 대신 쓴다 — SetWindowPos(SWP_SHOWWINDOW)는 WinForms의 캐시된 Visible을 갱신하지 않는다

    public HighlightFrame()
    {
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(0, 120, 215);   // Windows 강조색 계열
        Text = "WinDimmer Highlight";
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            // WS_EX_TRANSPARENT: 클릭 통과 + WindowFromPoint가 이 창을 반환하지 않게 한다
            cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    /// <summary>주어진 물리 픽셀 경계에 테두리를 맞춘다.</summary>
    public void ShowAt(Rect bounds)
    {
        if (bounds.IsEmpty) { HideFrame(); return; }
        if (bounds.Width <= Thickness * 2 || bounds.Height <= Thickness * 2) { HideFrame(); return; }
        if (!IsHandleCreated) CreateHandle();
        if (bounds == _bounds && _shown) return;

        _bounds = bounds;

        // 오버레이와 같은 이유로 Location/Size 대신 SetWindowPos에 물리 픽셀을 넘긴다
        User32.SetWindowPos(Handle, IntPtr.Zero,
            bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            SWP_NOACTIVATE | SWP_NOZORDER | SWP_SHOWWINDOW);
        _shown = true;

        ApplyRegion(bounds.Width, bounds.Height);
    }

    public void HideFrame()
    {
        if (!IsHandleCreated) return;
        _bounds = default;
        User32.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_HIDEWINDOW);
        _shown = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Region?.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>바깥 사각형에서 안쪽을 뺀 모양 — 가운데가 뚫린 테두리만 남는다.</summary>
    private void ApplyRegion(int width, int height)
    {
        using var outer = new GraphicsPath();
        outer.AddRectangle(new Rectangle(0, 0, width, height));
        using var inner = new GraphicsPath();
        inner.AddRectangle(new Rectangle(
            Thickness, Thickness, width - Thickness * 2, height - Thickness * 2));

        var region = new Region(outer);
        region.Exclude(inner);

        Region? previous = Region;
        Region = region;          // 최신 .NET의 Control.Region setter는 이전 Region을 알아서 Dispose하지만,
                                   // 그 사실에 기대지 않고 명시적으로 해제한다 — 안전하고 무해하다
        previous?.Dispose();
    }
}
