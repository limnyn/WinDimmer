using System.Drawing;

namespace WinDimmer;

/// <summary>
/// 단축키 한 개를 캡처하는 모달. 누르는 조합을 실시간으로 보여 주고,
/// 유효한 조합(수정키 1개 이상 + 실제 키)일 때만 확인을 허용한다.
/// ESC는 취소이고, 취소 시 <see cref="Result"/>는 생성자로 받은 현재 조합을 그대로 유지한다.
/// </summary>
internal sealed class HotkeyCaptureForm : Form
{
    // Control.Font는 WinForms가 자동으로 Dispose하지 않는다. 인스턴스마다 새로 만들면
    // 대화상자를 열 때마다 GDI 오브젝트가 샌다 (WindowPicker.HintWindow와 같은 패턴).
    private static readonly Font CaptureFont = new("Segoe UI", 20, FontStyle.Bold);

    private readonly Label _captureLabel = new();
    private readonly Label _hintLabel = new();
    private readonly Button _ok = new();

    private Keys _modifiers = Keys.None;
    private Keys _keyCode = Keys.None;
    private HotkeySpec? _pending;

    public HotkeyCaptureForm(string actionName, HotkeySpec current)
    {
        Result = current;

        Text = $"단축키 지정 — {actionName}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(360, 180);
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;
        KeyPreview = true;

        var infoLabel = new Label
        {
            Text = $"현재: {current.Display()}\n새 조합을 누르세요.",
            Dock = DockStyle.Top,
            Height = 48,
            TextAlign = ContentAlignment.MiddleCenter,
        };

        _captureLabel.Dock = DockStyle.Top;
        _captureLabel.Height = 56;
        _captureLabel.Font = CaptureFont;
        _captureLabel.TextAlign = ContentAlignment.MiddleCenter;

        _hintLabel.Text = "Ctrl, Alt, Shift 중 하나 이상을 함께 눌러야 합니다";
        _hintLabel.Dock = DockStyle.Top;
        _hintLabel.Height = 32;
        _hintLabel.ForeColor = Color.Firebrick;
        _hintLabel.TextAlign = ContentAlignment.MiddleCenter;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8),
        };
        _ok.Text = "확인";
        _ok.Width = 90;
        _ok.Enabled = false;
        _ok.DialogResult = DialogResult.OK;
        _ok.Click += (_, _) => { if (_pending is { } spec) Result = spec; };

        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Width = 90 };

        buttons.Controls.Add(_ok);
        buttons.Controls.Add(cancel);

        Controls.Add(_hintLabel);
        Controls.Add(_captureLabel);
        Controls.Add(infoLabel);
        Controls.Add(buttons);

        CancelButton = cancel;

        UpdateDisplay();
    }

    /// <summary>확인 시 캡처된 조합, 취소 시 생성자로 받은 조합.</summary>
    public HotkeySpec Result { get; private set; }

    /// <summary>
    /// 버튼의 기본 처리(예: 스페이스로 버튼 누르기)로 조합이 새어 나가지 않도록
    /// 모든 키 입력을 여기서 가로챈다.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        const int WM_KEYDOWN = 0x0100;
        const int WM_SYSKEYDOWN = 0x0104;

        if (msg.Msg is WM_KEYDOWN or WM_SYSKEYDOWN)
        {
            Keys modifiers = keyData & Keys.Modifiers;
            Keys keyCode = keyData & Keys.KeyCode;

            if (keyCode == Keys.Escape && modifiers == Keys.None)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }

            // 수정키 없는 Enter는 ESC와 같은 사정이다 — 수정키가 없어 유효한 단축키가 될 수 없다.
            // 그래서 캡처 입력으로 삼지 않고 OK와 같은 확인 동작으로 취급한다. 대기 중인 조합이
            // 없으면 아무 일도 하지 않는다 (실수로 눌러도 잃을 게 없다). 수정키가 있는 Enter
            // (예: Ctrl+Alt+Enter)는 평소대로 캡처된다.
            if (keyCode == Keys.Return && modifiers == Keys.None)
            {
                if (_pending is { } spec)
                {
                    Result = spec;
                    DialogResult = DialogResult.OK;
                    Close();
                }
                return true;
            }

            _modifiers = modifiers;
            _keyCode = keyCode;
            UpdateDisplay();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static bool IsModifierKeyCode(Keys keyCode) =>
        keyCode is Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin;

    /// <summary>
    /// 수정키만 눌린 중간 상태(Ctrl만 누른 순간, 또는 아직 아무것도 안 누른 상태)에서
    /// HotkeySpec.Display()를 그대로 쓰면 "Ctrl+Alt+None"처럼 어색하게 나온다.
    /// 실제 키가 없을 때는 눌려 있는 수정키만 나열하고 뒤에 "+"만 붙여 "입력 중"임을 보여 준다.
    /// </summary>
    private void UpdateDisplay()
    {
        bool hasRealKey = _keyCode != Keys.None && !IsModifierKeyCode(_keyCode);

        if (hasRealKey)
        {
            var candidate = new HotkeySpec(_modifiers | _keyCode);
            _captureLabel.Text = candidate.Display();

            if (candidate.IsValid)
            {
                _pending = candidate;
                _ok.Enabled = true;
                _hintLabel.Visible = false;
                return;
            }
        }
        else
        {
            _captureLabel.Text = ComposeModifiersOnly(_modifiers);
        }

        _pending = null;
        _ok.Enabled = false;
        _hintLabel.Visible = true;
    }

    private static string ComposeModifiersOnly(Keys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(Keys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(Keys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(Keys.Shift)) parts.Add("Shift");

        return parts.Count > 0 ? string.Join("+", parts) + "+" : "(키를 누르세요)";
    }
}
