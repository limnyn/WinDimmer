using System.ComponentModel;
using System.Drawing;

namespace WinDimmer;

/// <summary>
/// 통합 설정 창. 기본 밝기 · 디밍 중인 창 · 단축키 · 규칙 목록 · 자동 실행을 한 곳에 모은다.
/// 앱 전체에서 유일한 설정 모달이다.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly Action<byte> _onAlphaPreview;
    private readonly Action<Action<DimRule?>> _beginPickRule;
    private readonly Func<bool> _isPickInProgress;
    private readonly Action _suspendHotkeys;
    private readonly Action _resumeHotkeys;
    private readonly IDimmedWindowsView _dimmed;
    private readonly byte _entryAlpha;

    private readonly TrackBar _slider = new();
    private readonly Label _alphaLabel = new();
    private readonly ListView _dimmedList = new();
    private readonly TrackBar _dimmedSlider = new();
    private readonly Label _dimmedAlphaLabel = new();
    private readonly Button _releaseButton = new();
    private bool _suppressDimmedAlphaEvent;
    private readonly DataGridView _grid = new();
    private readonly BindingSource _source = new();
    private readonly BindingList<RuleRow> _rows;
    private readonly CheckBox _autoStartBox = new();
    private readonly ListView _hotkeyList = new();
    private readonly Dictionary<HotkeyAction, HotkeySpec> _hotkeyDraft;
    private readonly IReadOnlyDictionary<HotkeyAction, HotkeySpec> _registeredHotkeys;

    private Point _locationBeforePick;
    private bool _pickPending;

    public SettingsForm(
        DimConfig config,
        Action<byte> onAlphaPreview,
        Action<Action<DimRule?>> beginPickRule,
        Func<bool> isPickInProgress,
        Action suspendHotkeys,
        Action resumeHotkeys,
        IReadOnlyDictionary<HotkeyAction, HotkeySpec> registeredHotkeys,
        IDimmedWindowsView dimmed)
    {
        _onAlphaPreview = onAlphaPreview;
        _beginPickRule = beginPickRule;
        _isPickInProgress = isPickInProgress;
        _suspendHotkeys = suspendHotkeys;
        _resumeHotkeys = resumeHotkeys;
        _dimmed = dimmed;
        _entryAlpha = config.DefaultAlpha;
        _rows = new BindingList<RuleRow>(config.Rules.Select(RuleRow.From).ToList());
        _hotkeyDraft = new Dictionary<HotkeyAction, HotkeySpec>(RuleStore.ResolveHotkeys(config));
        _registeredHotkeys = registeredHotkeys;

        Text = "WinDimmer — 설정";
        StartPosition = FormStartPosition.CenterScreen;
        // 세로 여유가 필요하다 — 기본 밝기 80 + 단축키 180 + 규칙 220 + 하단 80을 빼고 남는
        // 자리가 전부 "디밍 중인 창" 목록이 된다. 720이면 목록에 160밖에 남지 않는다.
        ClientSize = new Size(760, 900);
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;   // 창에서 추가 중 화면 밖으로 옮겨도 작업표시줄·Alt-Tab에 남지 않게 한다
        FormBorderStyle = FormBorderStyle.Sizable;
        // MinimumSize는 핸들 생성 이후(OnHandleCreated)에 잡는다 — 여기서 잡으면 DPI 스케일링 전
        // 크기를 캡처해, 스케일된 모니터에서 실제 크기와 어긋난다.

        // --- 기본 밝기 섹션 ---
        var alphaGroup = new GroupBox
        {
            // 이름을 길게 쓴 이유: 이 슬라이더는 이미 어두운 창을 전부 덮어쓰지 않는다.
            // 창별로 따로 맞춰 둔 창은 건너뛰고, 나머지와 앞으로 새로 고를 창에만 적용된다.
            Text = "기본 밝기 (개별 조정하지 않은 창과 새로 선택할 창에 적용)",
            Dock = DockStyle.Top,
            Height = 80,
            Padding = new Padding(8),
        };

        _slider.Minimum = 0;
        _slider.Maximum = 255;
        _slider.TickStyle = TickStyle.None;
        _slider.Value = config.DefaultAlpha;
        _slider.Dock = DockStyle.Fill;
        _slider.ValueChanged += OnAlphaChanged;

        _alphaLabel.Text = config.DefaultAlpha.ToString();
        _alphaLabel.Dock = DockStyle.Right;
        _alphaLabel.Width = 40;
        _alphaLabel.TextAlign = ContentAlignment.MiddleCenter;

        var alphaPanel = new Panel { Dock = DockStyle.Fill };
        alphaPanel.Controls.Add(_slider);
        alphaPanel.Controls.Add(_alphaLabel);
        alphaGroup.Controls.Add(alphaPanel);

        // --- 디밍 중인 창 섹션 ---
        // 창별 밝기는 확인/취소와 무관하게 즉시 적용된다. 규칙·단축키처럼 초안을 두지 않는 이유는
        // 이 값이 설정 파일에 저장되지 않는 화면상의 상태이기 때문이다 — 창이 닫히면 함께 사라진다.
        var dimmedGroup = new GroupBox
        {
            Text = "디밍 중인 창 (밝기 변경은 즉시 적용)",
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
        };

        _dimmedList.Dock = DockStyle.Fill;
        _dimmedList.View = View.Details;
        _dimmedList.FullRowSelect = true;
        _dimmedList.MultiSelect = false;
        _dimmedList.HideSelection = false;
        _dimmedList.Columns.Add("프로세스", 140);
        _dimmedList.Columns.Add("창 제목", 380);
        _dimmedList.Columns.Add("밝기", 70);
        _dimmedList.SelectedIndexChanged += OnDimmedSelectionChanged;

        _dimmedSlider.Minimum = 0;
        _dimmedSlider.Maximum = 255;
        _dimmedSlider.TickStyle = TickStyle.None;
        _dimmedSlider.Dock = DockStyle.Fill;
        _dimmedSlider.ValueChanged += OnDimmedAlphaChanged;

        var dimmedSliderCaption = new Label
        {
            Text = "선택한 창 밝기",
            Dock = DockStyle.Left,
            Width = 100,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _dimmedAlphaLabel.Dock = DockStyle.Right;
        _dimmedAlphaLabel.Width = 40;
        _dimmedAlphaLabel.TextAlign = ContentAlignment.MiddleCenter;

        // Fill을 먼저 넣어야 Left/Right가 그 위를 덮지 않는다.
        var dimmedSliderRow = new Panel { Dock = DockStyle.Fill };
        dimmedSliderRow.Controls.Add(_dimmedSlider);
        dimmedSliderRow.Controls.Add(_dimmedAlphaLabel);
        dimmedSliderRow.Controls.Add(dimmedSliderCaption);

        _releaseButton.Text = "선택한 창 해제";
        _releaseButton.Width = 120;
        _releaseButton.Click += OnReleaseDimmedWindow;

        var dimmedButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 40,
            Padding = new Padding(4),
        };
        dimmedButtons.Controls.Add(_releaseButton);

        var dimmedBottom = new Panel { Dock = DockStyle.Bottom, Height = 80 };
        dimmedBottom.Controls.Add(dimmedSliderRow);
        dimmedBottom.Controls.Add(dimmedButtons);

        dimmedGroup.Controls.Add(_dimmedList);
        dimmedGroup.Controls.Add(dimmedBottom);

        RefreshDimmedList();
        _dimmed.Changed += OnDimmedWindowsChanged;

        // --- 규칙 목록 섹션 ---
        var rulesGroup = new GroupBox
        {
            Text = "규칙 목록",
            Dock = DockStyle.Bottom,
            Height = 220,
            Padding = new Padding(8),
        };

        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = true;
        _grid.AllowUserToAddRows = true;
        _grid.AllowUserToDeleteRows = true;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _source.DataSource = _rows;
        _grid.DataSource = _source;

        var ruleButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 40,
            Padding = new Padding(4),
        };
        var addFromWindow = new Button { Text = "창에서 추가", Width = 110 };
        var delete = new Button { Text = "삭제", Width = 90 };
        addFromWindow.Click += OnAddFromWindow;
        delete.Click += OnDeleteRow;
        ruleButtons.Controls.Add(addFromWindow);
        ruleButtons.Controls.Add(delete);

        rulesGroup.Controls.Add(_grid);
        rulesGroup.Controls.Add(ruleButtons);

        // --- 단축키 섹션 ---
        var hotkeyGroup = new GroupBox
        {
            Text = "단축키",
            Dock = DockStyle.Top,
            Height = 180,
            Padding = new Padding(8),
        };

        _hotkeyList.Dock = DockStyle.Fill;
        _hotkeyList.View = View.Details;
        _hotkeyList.FullRowSelect = true;
        _hotkeyList.MultiSelect = false;
        _hotkeyList.HideSelection = false;
        _hotkeyList.Columns.Add("동작", 220);
        _hotkeyList.Columns.Add("단축키", 200);

        var hotkeyButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 40,
            Padding = new Padding(4),
        };
        var changeHotkey = new Button { Text = "변경", Width = 90 };
        var resetHotkeys = new Button { Text = "기본값으로 되돌리기", Width = 150 };
        changeHotkey.Click += OnChangeHotkey;
        resetHotkeys.Click += OnResetHotkeys;
        hotkeyButtons.Controls.Add(changeHotkey);
        hotkeyButtons.Controls.Add(resetHotkeys);

        hotkeyGroup.Controls.Add(_hotkeyList);
        hotkeyGroup.Controls.Add(hotkeyButtons);

        RefreshHotkeyList();

        // --- 자동 실행 섹션 ---
        _autoStartBox.Text = "시작 시 자동 실행";
        _autoStartBox.Checked = config.AutoStart;
        _autoStartBox.AutoSize = false;
        _autoStartBox.Dock = DockStyle.Top;
        _autoStartBox.Height = 32;
        _autoStartBox.Padding = new Padding(8, 6, 8, 6);

        // --- 확인/취소 버튼 ---
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 48,
            Padding = new Padding(8),
        };
        var ok = new Button { Text = "확인", DialogResult = DialogResult.OK, Width = 90 };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Width = 90 };
        ok.Click += OnOk;
        // RightToLeft 흐름이라 먼저 넣은 것이 오른쪽 끝에 놓인다: …[버전] [취소] [확인]
        var versionLabel = new Label
        {
            Text = $"WinDimmer {AppVersion.Display}",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 14, 12, 0),
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(versionLabel);

        // 자동 실행 체크박스와 확인/취소 버튼을 하나의 하단 패널로 묶는다.
        // 서로 다른 가장자리(Top/Bottom)에 도킹하므로 추가 순서와 무관하게 겹치지 않는다.
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 80 };
        footer.Controls.Add(buttons);
        footer.Controls.Add(_autoStartBox);

        // 추가 순서가 곧 배치다. Fill을 맨 먼저 넣어야 나머지 도킹 영역을 침범하지 않고,
        // 같은 가장자리에 여러 개를 붙일 때는 나중에 넣은 것이 가장자리에 더 가깝게 놓인다.
        // 그래서 footer는 rulesGroup보다 뒤에 넣어야 창 맨 아래를 차지한다.
        Controls.Add(dimmedGroup);
        Controls.Add(rulesGroup);
        Controls.Add(footer);
        Controls.Add(hotkeyGroup);
        Controls.Add(alphaGroup);
        AcceptButton = ok;
        CancelButton = cancel;

        FormClosing += OnFormClosing;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        MinimumSize = Size;   // 그리드/하단 영역이 겹칠 정도로 줄어드는 것을 막는다 (외곽 크기 기준이라야 ClientSize와 어긋나지 않는다)
    }

    public List<DimRule> Rules { get; private set; } = new();
    public bool AutoStart { get; private set; }
    public byte Alpha { get; private set; }
    public IReadOnlyDictionary<HotkeyAction, HotkeySpec> Hotkeys { get; private set; } =
        new Dictionary<HotkeyAction, HotkeySpec>();

    /// <summary>
    /// 밖에서 밝기가 바뀌었을 때(핫키·기본 밝기 적용) 목록을 현재 값으로 맞춘다.
    /// 창 목록 자체가 바뀌지 않았으면 항목을 다시 만들지 않고 숫자만 갱신한다 —
    /// 램프를 도는 동안 80ms마다 목록을 새로 만들면 선택이 풀리고 눈에 띄게 깜빡인다.
    /// </summary>
    public void SyncDimmedWindows()
    {
        if (IsDisposed) return;
        RefreshDimmedList();
    }

    private void RefreshDimmedList()
    {
        IReadOnlyList<DimmedWindow> windows = _dimmed.Snapshot();

        if (SameHandlesAsListed(windows))
        {
            UpdateListedValues(windows);
            SyncDimmedSliderToSelection();
            return;
        }

        IntPtr previouslySelected = SelectedHandle();

        _dimmedList.BeginUpdate();
        try
        {
            _dimmedList.Items.Clear();
            foreach (DimmedWindow w in windows)
            {
                var item = new ListViewItem(w.Process) { Tag = w.Handle };
                item.SubItems.Add(w.Title);
                item.SubItems.Add(TextForAlpha(w));
                _dimmedList.Items.Add(item);
            }
        }
        finally
        {
            _dimmedList.EndUpdate();
        }

        // 목록이 바뀌어도 보고 있던 창이 아직 남아 있으면 선택을 잃지 않게 되살린다.
        SelectHandle(previouslySelected);
        SyncDimmedSliderToSelection();
    }

    /// <summary>개별 조정한 창은 값 뒤에 별표를 붙여 기본 밝기를 따라가지 않는다는 것을 드러낸다.</summary>
    private static string TextForAlpha(DimmedWindow w) =>
        w.AlphaIsCustom ? $"{w.Alpha} *" : w.Alpha.ToString();

    private bool SameHandlesAsListed(IReadOnlyList<DimmedWindow> windows)
    {
        if (_dimmedList.Items.Count != windows.Count) return false;

        for (int i = 0; i < windows.Count; i++)
        {
            if (_dimmedList.Items[i].Tag is not IntPtr handle || handle != windows[i].Handle)
                return false;
        }
        return true;
    }

    private void UpdateListedValues(IReadOnlyList<DimmedWindow> windows)
    {
        for (int i = 0; i < windows.Count; i++)
        {
            ListViewItem item = _dimmedList.Items[i];
            string title = windows[i].Title;
            string alpha = TextForAlpha(windows[i]);

            // 같은 값을 다시 대입해도 ListView는 다시 그린다. 달라졌을 때만 건드린다.
            if (item.SubItems[1].Text != title) item.SubItems[1].Text = title;
            if (item.SubItems[2].Text != alpha) item.SubItems[2].Text = alpha;
        }
    }

    private IntPtr SelectedHandle() =>
        _dimmedList.SelectedItems.Count == 1 && _dimmedList.SelectedItems[0].Tag is IntPtr handle
            ? handle
            : IntPtr.Zero;

    private void SelectHandle(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;

        foreach (ListViewItem item in _dimmedList.Items)
        {
            if (item.Tag is IntPtr h && h == handle)
            {
                item.Selected = true;
                return;
            }
        }
    }

    /// <summary>선택된 창이 없으면 슬라이더와 해제 버튼을 잠근다 — 대상 없는 조작을 막는다.</summary>
    private void SyncDimmedSliderToSelection()
    {
        IntPtr handle = SelectedHandle();
        bool hasSelection = handle != IntPtr.Zero;

        _dimmedSlider.Enabled = hasSelection;
        _releaseButton.Enabled = hasSelection;

        if (!hasSelection)
        {
            _dimmedAlphaLabel.Text = "—";
            return;
        }

        DimmedWindow? window = _dimmed.Snapshot().FirstOrDefault(w => w.Handle == handle);
        if (window is null) return;

        // 슬라이더에 값을 넣는 것 자체가 ValueChanged를 부른다. 그대로 두면 목록을 고르기만 해도
        // 그 창의 밝기를 "사용자가 지정했다"로 표시해 버리므로 억제한다.
        _suppressDimmedAlphaEvent = true;
        try
        {
            _dimmedSlider.Value = window.Value.Alpha;
            _dimmedAlphaLabel.Text = window.Value.Alpha.ToString();
        }
        finally
        {
            _suppressDimmedAlphaEvent = false;
        }
    }

    private void OnDimmedWindowsChanged() => SyncDimmedWindows();

    private void OnDimmedSelectionChanged(object? sender, EventArgs e) => SyncDimmedSliderToSelection();

    private void OnDimmedAlphaChanged(object? sender, EventArgs e)
    {
        _dimmedAlphaLabel.Text = _dimmedSlider.Value.ToString();
        if (_suppressDimmedAlphaEvent) return;

        IntPtr handle = SelectedHandle();
        if (handle == IntPtr.Zero) return;

        _dimmed.SetAlpha(handle, (byte)_dimmedSlider.Value);
        RefreshDimmedList();
    }

    private void OnReleaseDimmedWindow(object? sender, EventArgs e)
    {
        IntPtr handle = SelectedHandle();
        if (handle == IntPtr.Zero) return;

        _dimmed.Release(handle);   // Changed 이벤트가 목록을 다시 그린다
    }

    private void OnAlphaChanged(object? sender, EventArgs e)
    {
        // 되먹임 억제가 필요했던 것은 핫키가 이 슬라이더를 되돌려 놓던 시절 얘기다.
        // 밝기 핫키는 이제 포그라운드 창 하나만 건드리므로 여기로 돌아오지 않는다.
        _alphaLabel.Text = _slider.Value.ToString();
        _onAlphaPreview((byte)_slider.Value);
    }

    private void OnAddFromWindow(object? sender, EventArgs e)
    {
        if (_pickPending) return;   // 진행 중이면 복원 좌표를 덮어쓰지 않는다

        // 트레이 메뉴는 모달에 막히지 않으므로, 설정 창이 열린 채로 "창 선택"을 먼저 시작해 둘 수 있다.
        // 그 상태에서 폼을 화면 밖으로 옮기고 나면 콜백은 즉시 null로 오는데, 그 사이의 이동·비활성화가
        // 사용자에게는 원인 불명의 깜빡임으로만 보인다. 옮기기 전에 미리 걸러 안내한다.
        if (_isPickInProgress())
        {
            MessageBox.Show(
                "이미 다른 창 선택이 진행 중입니다. 먼저 끝내거나 ESC로 취소한 뒤 다시 시도하세요.",
                "WinDimmer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _pickPending = true;

        // Hide()는 ShowDialog 루프를 끝내 버린다. 화면 밖으로 옮겨 모달을 살려 둔다.
        // Opacity=0 은 쓸 수 없다 — 보이지 않아도 WindowFromPoint에 잡혀 자기 자신이 선택된다.
        // Enabled=false 를 함께 두는 것이 중요하다 — 화면 밖이어도 폼은 포커스를 유지하므로,
        // 선택 취소용 ESC가 이 폼에도 전달돼 CancelButton이 눌리고 편집 내용이 버려진다.
        _locationBeforePick = Location;
        Enabled = false;
        Location = new Point(-32000, -32000);

        _beginPickRule(rule =>
        {
            _pickPending = false;
            if (IsDisposed) return;          // 대기 중 폼이 사라졌으면 아무것도 하지 않는다

            Location = _locationBeforePick;
            Enabled = true;
            Activate();
            if (rule is not null) AddRow(rule);
        });
    }

    private void AddRow(DimRule rule) => _rows.Add(RuleRow.From(rule));

    private void RefreshHotkeyList()
    {
        int selectedIndex = _hotkeyList.SelectedIndices.Count > 0 ? _hotkeyList.SelectedIndices[0] : -1;

        _hotkeyList.BeginUpdate();
        _hotkeyList.Items.Clear();
        foreach (HotkeyAction action in HotkeyActions.All)
        {
            var item = new ListViewItem(HotkeyActions.DisplayName(action));
            string text = _hotkeyDraft[action].Display();
            // 실제로 OS에 등록돼 있지 않은 동작은 표시로만 알 수 없으므로 명시적으로 표시한다 —
            // 그래야 사용자가 "확인"을 다시 눌러 재시도해야 한다는 걸 알 수 있다.
            if (!_registeredHotkeys.ContainsKey(action)) text += " (등록 실패)";
            item.SubItems.Add(text);
            _hotkeyList.Items.Add(item);
        }
        _hotkeyList.EndUpdate();

        if (selectedIndex >= 0 && selectedIndex < _hotkeyList.Items.Count)
            _hotkeyList.Items[selectedIndex].Selected = true;
    }

    private void OnChangeHotkey(object? sender, EventArgs e)
    {
        if (_hotkeyList.SelectedIndices.Count == 0) return;   // 선택 없으면 아무 일도 하지 않는다

        HotkeyAction action = HotkeyActions.All[_hotkeyList.SelectedIndices[0]];

        // 캡처 창이 떠 있는 동안 앱 전역 단축키(예: Ctrl+Alt+↑)가 그대로 눌리면 밝기가 바뀌어 버린다.
        // 예외가 나도 단축키가 죽은 채 남지 않도록 finally에서 반드시 재개한다.
        try
        {
            _suspendHotkeys();
            using var capture = new HotkeyCaptureForm(HotkeyActions.DisplayName(action), _hotkeyDraft[action]);
            if (capture.ShowDialog(this) == DialogResult.OK)
            {
                _hotkeyDraft[action] = capture.Result;
                RefreshHotkeyList();
            }
        }
        finally
        {
            _resumeHotkeys();
        }
    }

    private void OnResetHotkeys(object? sender, EventArgs e)
    {
        foreach (HotkeyAction action in HotkeyActions.All)
            _hotkeyDraft[action] = HotkeyActions.Default(action);

        RefreshHotkeyList();
    }

    private void OnDeleteRow(object? sender, EventArgs e)
    {
        IEnumerable<DataGridViewRow> targets = _grid.SelectedRows.Count > 0
            ? _grid.SelectedRows.Cast<DataGridViewRow>()
            : _grid.CurrentRow is { IsNewRow: false } current ? new[] { current } : Array.Empty<DataGridViewRow>();

        foreach (DataGridViewRow row in targets)
        {
            if (row.DataBoundItem is RuleRow rr) _rows.Remove(rr);
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        // 이 폼이 사라진 뒤에도 이벤트가 오면 폐기된 컨트롤을 만지게 된다.
        _dimmed.Changed -= OnDimmedWindowsChanged;

        // 창별 밝기는 즉시 적용이라 되돌리지 않는다. 취소가 되돌리는 것은 기본 밝기뿐이다.
        if (DialogResult != DialogResult.OK) _onAlphaPreview(_entryAlpha);
    }

    private void OnOk(object? sender, EventArgs e)
    {
        _grid.EndEdit();
        _source.EndEdit();

        RuleRowMapResult mapped = RuleRowMapper.Map(_rows.Select(r =>
            new RuleRowValues(r.프로세스명, r.제목정규식, r.밝기, r.사용)));

        if (!mapped.IsSuccess)
        {
            MessageBox.Show(
                $"정규식이 잘못되었습니다: {mapped.InvalidPattern}",
                "WinDimmer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        // 두 동작이 같은 조합을 가리키면 등록 시점에 나중 것이 앞선 것을 밀어내 버리므로,
        // 여기서 미리 걸러 사용자가 고치게 한다.
        var duplicateGroups = _hotkeyDraft
            .GroupBy(kv => kv.Value)
            .Where(g => g.Count() > 1)
            .ToList();

        if (duplicateGroups.Count > 0)
        {
            string message = string.Join("\n", duplicateGroups.Select(g =>
                $"{g.Key.Display()}: {string.Join(", ", g.Select(kv => HotkeyActions.DisplayName(kv.Key)))}"));

            MessageBox.Show(
                $"같은 조합이 여러 동작에 지정되어 있습니다:\n{message}",
                "WinDimmer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        Rules = mapped.Rules;
        AutoStart = _autoStartBox.Checked;
        Alpha = (byte)_slider.Value;
        Hotkeys = new Dictionary<HotkeyAction, HotkeySpec>(_hotkeyDraft);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _source.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>DataGridView가 열 제목으로 쓰는 이름이라 한국어로 둔다.</summary>
    public sealed class RuleRow
    {
        public string 프로세스명 { get; set; } = string.Empty;
        public string 제목정규식 { get; set; } = string.Empty;
        public int 밝기 { get; set; } = AlphaMath.Default;
        public bool 사용 { get; set; } = true;

        public static RuleRow From(DimRule r) => new()
        {
            프로세스명 = r.ProcessName,
            제목정규식 = r.TitlePattern,
            밝기 = r.Alpha,
            사용 = r.Enabled,
        };
    }
}
