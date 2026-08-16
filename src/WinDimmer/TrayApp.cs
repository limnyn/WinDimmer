using System.Diagnostics;
using System.Drawing;
using System.Text;
using Microsoft.Win32;
using Windows.ApplicationModel;
using WinDimmer.Native;
using static WinDimmer.Native.Constants;

namespace WinDimmer;

internal sealed class TrayApp : IDisposable, IDimmedWindowsView
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "WinDimmer";

    private readonly DimManager _manager;
    private readonly NotifyIcon _icon;
    private readonly WindowPicker _picker;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _brightnessMenuItem;
    private readonly ToolStripMenuItem _blackoutMenuItem;
    private readonly ToolStripMenuItem _clearAllMenuItem;
    private readonly HotkeyWindow _hotkeys;
    private readonly RuleWatcher _watcher;
    private readonly Icon _idleIcon;
    private readonly Icon _activeIcon;
    private readonly AlphaRamp _alphaRamp;
    private bool _disposed;
    private SettingsForm? _settings;
    private bool _suppressElevatedBalloon;

    private enum PickPurpose { Dim, Rule }

    private PickPurpose _pickPurpose = PickPurpose.Dim;
    private Action<DimRule?>? _pendingRuleCallback;

    public DimConfig Config { get; private set; }

    /// <summary>이미 창 선택이 진행 중인지. 설정 창이 "창에서 추가" 충돌을 미리 감지하는 데 쓴다.</summary>
    public bool IsPickInProgress => _picker.IsActive;

    public TrayApp(DimManager manager)
    {
        _manager = manager;
        Config = RuleStore.Load();

        _picker = new WindowPicker();
        _picker.Picked += OnPicked;
        _picker.Cancelled += OnPickerCancelled;

        _alphaRamp = new AlphaRamp();

        _brightnessMenuItem = new ToolStripMenuItem(string.Empty) { Enabled = false };
        _blackoutMenuItem = new ToolStripMenuItem(string.Empty) { Enabled = false };
        _clearAllMenuItem = new ToolStripMenuItem(string.Empty) { Enabled = false };

        _menu = new ContextMenuStrip();
        _menu.Items.Add(new ToolStripMenuItem("창 선택", null, (_, _) => StartDimPick()));
        _menu.Items.Add(new ToolStripMenuItem("설정…", null, (_, _) => ShowSettings()));
        _menu.Items.Add(_brightnessMenuItem);
        _menu.Items.Add(_blackoutMenuItem);
        _menu.Items.Add(_clearAllMenuItem);
        _menu.Items.Add(new ToolStripMenuItem("진단 정보 복사", null, (_, _) => CopyDiagnostics()));
        _menu.Items.Add(new ToolStripSeparator());
        // 클릭할 수 없는 정보 항목. 버그 제보를 받을 때 어떤 버전인지 되물을 필요가 없게 한다.
        _menu.Items.Add(new ToolStripMenuItem($"WinDimmer {AppVersion.Display}") { Enabled = false });
        _menu.Items.Add(new ToolStripMenuItem("종료", null, (_, _) => Application.Exit()));

        int iconSize = IconFactory.SmallIconSize();
        // 이름이 뒤집힌 것처럼 보이지만 맞다 — 밝은 테마 배경 위에서는 잉크를 어둡게 그려야 보인다
        bool darkInk = IconFactory.SystemUsesLightTheme();
        _idleIcon = IconFactory.Create(active: false, iconSize, darkInk);
        _activeIcon = IconFactory.Create(active: true, iconSize, darkInk);

        _icon = new NotifyIcon
        {
            Icon = _idleIcon,
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _icon.DoubleClick += (_, _) => StartDimPick();

        _manager.SessionsChanged += UpdateTrayIcon;
        _manager.ElevatedTargetDetected += OnElevatedTarget;
        UpdateTrayIcon();

        _hotkeys = new HotkeyWindow();
        _hotkeys.Pressed += OnHotkeyPressed;
        ApplyHotkeys();

        _watcher = new RuleWatcher();
        _watcher.RuleMatched += (hwnd, rule) =>
        {
            if (!_manager.IsDimmed(hwnd)) _manager.TryDim(hwnd, rule.Alpha);
        };
        // 사용자가 예전에 직접 디밍했던 프로세스의 창이 (재시작 후든, 그 프로세스 자체가
        // 재시작된 것이든) 다시 나타나면 기본 밝기로 되살린다. 규칙과 달리 알파를 고정해
        // 저장하지 않으므로, 매칭 시점의 현재 기본 밝기를 그대로 쓴다.
        // 이 경로는 사용자가 지금 막 선택한 게 아니라 자동 복원이므로, 상승 권한 안내 풍선은
        // 여기서 발생한 디밍에 한해 억제한다 — 그러지 않으면 로그인할 때마다 같은 안내가 뜬다.
        _watcher.AutoDimMatched += hwnd =>
        {
            if (_manager.IsDimmed(hwnd)) return;

            _suppressElevatedBalloon = true;
            try { _manager.TryDim(hwnd, Config.DefaultAlpha); }
            finally { _suppressElevatedBalloon = false; }
        };
        _watcher.UpdateRules(Config.Rules);
        _watcher.UpdateAutoDimProcesses(Config.AutoDimProcesses);
        _watcher.ApplyToExistingWindows();

        WarnAboutInvalidRules();
    }

    // --- IDimmedWindowsView ---
    // 설정 창이 디밍 중인 창들을 보고 조작하는 창구. 세션 조작은 전부 DimManager 한 곳을 거친다.

    event Action? IDimmedWindowsView.Changed
    {
        add => _manager.SessionsChanged += value;
        remove => _manager.SessionsChanged -= value;
    }

    IReadOnlyList<DimmedWindow> IDimmedWindowsView.Snapshot() =>
        _manager.Sessions
            .Select(s => new DimmedWindow(
                s.Target,
                WindowInspector.GetTitle(s.Target),
                WindowInspector.GetProcessName(s.Target),
                s.Alpha,
                s.AlphaIsCustom))
            // 같은 프로그램의 창이 흩어져 보이지 않게 프로세스명으로 먼저 묶는다.
            .OrderBy(w => w.Process, StringComparer.OrdinalIgnoreCase)
            .ThenBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    void IDimmedWindowsView.SetAlpha(IntPtr target, byte alpha) => _manager.SetAlpha(target, alpha);

    // 목록에서 해제할 때도 자동 복원 목록 정리가 필요하므로 핫키·창 선택과 같은 경로를 쓴다.
    void IDimmedWindowsView.Release(IntPtr target)
    {
        if (_manager.IsDimmed(target)) ManualDimToggle(target);
    }

    public void ShowBalloon(string title, string text) =>
        _icon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);

    public void SaveConfig() => RuleStore.Save(Config);

    private void OnPicked(IntPtr hwnd)
    {
        if (_pickPurpose == PickPurpose.Rule)
        {
            CompleteRulePick(RuleDraft.FromWindow(
                WindowInspector.GetProcessName(hwnd), Config.DefaultAlpha));
            return;
        }

        ManualDimToggle(hwnd);
    }

    // SetWindowsHookExW 등록 실패 시 발생한다. 구독하지 않으면
    // 창 선택이 아무 피드백 없이 조용히 아무것도 하지 않게 된다.
    private void OnPickerCancelled()
    {
        if (_pickPurpose == PickPurpose.Rule)
        {
            CompleteRulePick(null);
            return;
        }

        ShowBalloon("WinDimmer", "창 선택을 시작하지 못했습니다.");
    }

    /// <summary>
    /// 기본 밝기를 바꾼다. 앞으로 새로 디밍하는 창의 시작값이 되고, 이미 디밍 중인 창 중에서는
    /// 사용자가 개별적으로 밝기를 지정하지 않은 창에만 적용된다 — 창마다 맞춰 둔 값을 지우지 않기 위해서다.
    /// </summary>
    public void SetDefaultAlpha(byte alpha)
    {
        Config = Config with { DefaultAlpha = alpha };
        _manager.ApplyDefaultAlpha(alpha);
        _settings?.SyncDimmedWindows();
    }

    /// <summary>
    /// 창 하나의 밝기를 한 단계 조정하고, 값이 실제로 바뀌었는지 반환한다.
    /// 반환값은 밝기 램프의 종료 조건이다 — 한계에 닿았거나 대상 창이 그새 해제됐으면 false.
    /// </summary>
    private bool AdjustSessionAlpha(IntPtr target, int delta)
    {
        DimSession? session = _manager.Find(target);
        if (session is null) return false;

        byte next = AlphaMath.Adjust(session.Alpha, delta);
        if (next == session.Alpha) return false;

        _manager.SetAlpha(target, next);
        _settings?.SyncDimmedWindows();
        return true;
    }

    /// <summary>맵에 없으면 기본 조합으로 돌아간다 — 등록에 실패한 동작도 램프 판정은 해야 한다.</summary>
    private HotkeySpec SpecFor(HotkeyAction action) =>
        _hotkeys.Current.TryGetValue(action, out HotkeySpec spec) ? spec : HotkeyActions.Default(action);

    /// <summary>
    /// 밝기 핫키를 포그라운드 창 하나에만 적용한다. 그 창이 디밍 중이 아니면 아무 일도 하지 않는다 —
    /// 어둡지 않은 창 위에서 누른 밝기 키가 다른 창들의 밝기를 건드리는 것은 사용자가 기대할 수 없는
    /// 결과이고, 화면에 아무 변화가 없으니 무엇이 바뀌었는지 알 방법도 없다.
    /// </summary>
    private void BeginAlphaRamp(HotkeyAction action, int delta)
    {
        IntPtr target = User32.GetForegroundWindow();
        if (!_manager.IsDimmed(target)) return;

        _alphaRamp.Begin(SpecFor(action), delta, d => AdjustSessionAlpha(target, d));
    }

    private void OnHotkeyPressed(HotkeyAction action)
    {
        // 밝기 외의 동작이 끼어들면 돌고 있던 램프는 즉시 끝낸다.
        if (action is not (HotkeyAction.Brighter or HotkeyAction.Darker)) _alphaRamp.Stop();

        switch (action)
        {
            case HotkeyAction.Toggle:
                ManualDimToggle(User32.GetForegroundWindow());
                break;
            case HotkeyAction.Brighter:
                // 부호가 뒤집혔다 — 밝게는 알파를 낮춘다.
                BeginAlphaRamp(action, -AlphaMath.Step);
                break;
            case HotkeyAction.Darker:
                BeginAlphaRamp(action, AlphaMath.Step);
                break;
            case HotkeyAction.Blackout:
                _manager.ToggleBlackout(User32.GetForegroundWindow());
                break;
            case HotkeyAction.ClearAll:
                ReleaseAll();
                break;
            case HotkeyAction.Pick:
                StartDimPick();
                break;
        }
    }

    /// <summary>
    /// 디밍 목적의 창 선택을 시작하는 유일한 진입점. 트레이 메뉴·아이콘 더블클릭·핫키가 모두
    /// 여기를 거친다. 목적을 매번 <see cref="PickPurpose.Dim"/>으로 되돌리는 것이 핵심이다 —
    /// 규칙 추가용 선택이 비정상 종료돼 목적이 Rule로 남아 있으면, 다음에 고른 창이 디밍되지
    /// 않고 규칙 편집으로 새기 때문이다.
    ///
    /// 여기서 전체 해제를 겸하지 않는 이유: 전체 해제는 이미 <see cref="HotkeyAction.ClearAll"/>
    /// 전용 단축키가 있고, 겸용으로 두면 이미 어두운 창이 하나라도 있을 때 두 번째 창을 추가로
    /// 선택하는 길이 아예 막힌다. 이미 어두운 창을 다시 고르면 <see cref="ManualDimToggle"/>가
    /// 토글이므로 그 창만 해제된다.
    /// </summary>
    private void StartDimPick()
    {
        if (_picker.IsActive) return;

        _pickPurpose = PickPurpose.Dim;
        _picker.Start();
    }

    /// <summary>
    /// 창 선택/핫키로 창 하나를 수동으로 켜고 끄는 유일한 진입점. 토글 결과(켜졌는지 꺼졌는지)에
    /// 따라 그 창의 프로세스 이름을 <see cref="DimConfig.AutoDimProcesses"/>에 추가하거나 제거해,
    /// 재시작 후에도(또는 대상 프로세스 자체가 재시작된 뒤에도) 같은 프로세스가 다시 디밍되게 한다.
    /// 매칭이 프로세스 이름 단위라 같은 프로그램의 창이 여럿일 수 있으므로, 해제 시에는 그 프로세스의
    /// 다른 창이 여전히 디밍 중인지 확인한 뒤에만 목록에서 지운다 — 그러지 않으면 창 A를 해제하는
    /// 순간 화면에 여전히 디밍돼 있는 창 B의 기억을 잃는다.
    /// </summary>
    private void ManualDimToggle(IntPtr hwnd)
    {
        string process = WindowInspector.GetProcessName(hwnd);
        bool wasDimmed = _manager.IsDimmed(hwnd);

        _manager.Toggle(hwnd, Config.DefaultAlpha);

        if (process.Length == 0) return;

        bool isDimmed = _manager.IsDimmed(hwnd);
        if (wasDimmed == isDimmed) return;   // 토글이 실패했거나(대상이 이미 사라짐 등) 변화 없음

        if (isDimmed)
        {
            UpdateAutoDimProcesses(AutoDimList.Add(Config.AutoDimProcesses, process));
            return;
        }

        if (StillHasSessionFor(process)) return;   // 같은 프로세스의 다른 창이 아직 디밍 중 — 기억을 지우지 않는다

        UpdateAutoDimProcesses(AutoDimList.Remove(Config.AutoDimProcesses, process));
    }

    /// <summary>
    /// 남아 있는 세션 중 프로세스 이름이 일치하는 것이 있는지(대소문자 무시) 검사한다.
    /// <see cref="AutoDimList.Contains"/>의 대소문자 무시 규칙을 그대로 재사용한다.
    /// </summary>
    private bool StillHasSessionFor(string processName)
    {
        IReadOnlyList<string> remainingProcesses = _manager.Sessions
            .Select(s => WindowInspector.GetProcessName(s.Target))
            .ToList();

        return AutoDimList.Contains(remainingProcesses, processName);
    }

    /// <summary>
    /// 디밍 중인 모든 창을 해제하고(<see cref="DimManager.UndimAll"/>), 해제된 창들의 프로세스
    /// 이름을 자동 복원 목록에서도 제거한다. "전체 해제"가 사용자가 손으로 쓴 규칙(Config.Rules)은
    /// 절대 건드리지 않는 이유가 바로 이 분리 — 자동 복원 목록만 지운다.
    /// </summary>
    private void ReleaseAll()
    {
        string[] processes = _manager.Sessions
            .Select(s => WindowInspector.GetProcessName(s.Target))
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _manager.UndimAll();

        if (processes.Length == 0) return;

        IReadOnlyList<string> updated = Config.AutoDimProcesses;
        foreach (string process in processes) updated = AutoDimList.Remove(updated, process);
        UpdateAutoDimProcesses(updated);
    }

    /// <summary>
    /// 자동 복원 목록을 갱신하고, 감시자에 반영하고, 저장한다.
    /// 디밍/해제할 때마다 호출되므로 저장 실패가 앱을 죽이면 안 된다.
    /// </summary>
    private void UpdateAutoDimProcesses(IReadOnlyList<string> processes)
    {
        Config = Config with { AutoDimProcesses = processes.ToList() };
        _watcher.UpdateAutoDimProcesses(Config.AutoDimProcesses);

        try { SaveConfig(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"자동 디밍 목록 저장 실패 — {ex}");
        }
    }

    /// <summary>현재 설정의 단축키를 등록하고, 실패 항목을 알리고, 메뉴 안내 문구를 갱신한다.</summary>
    private void ApplyHotkeys()
    {
        IReadOnlyDictionary<HotkeyAction, HotkeySpec> hotkeys = RuleStore.ResolveHotkeys(Config);
        (IReadOnlyDictionary<HotkeyAction, HotkeySpec> applied, IReadOnlyList<HotkeyAction> failed) =
            ApplyDesiredHotkeys(hotkeys);

        // 메뉴 안내 문구는 원하던 조합이 아니라 실제로 등록된 조합을 따라야 한다.
        UpdateHotkeyMenuText(applied);

        if (failed.Count > 0)
        {
            string names = string.Join(", ", failed.Select(a =>
                $"{HotkeyActions.DisplayName(a)}({DisplayFor(hotkeys, a)})"));
            ShowBalloon("WinDimmer", $"다른 프로그램이 사용 중이라 등록하지 못한 단축키: {names}");
        }
    }

    /// <summary>
    /// 원하는 단축키 집합을 등록한다. <see cref="HotkeyWindow.TryApply"/>는 원자적이다 —
    /// 넷 중 하나만 충돌해도 실패 목록엔 그 하나만 담겨 돌아오지만, 등록 자체는 이전 설정으로
    /// 전부 롤백된다. 실패한 항목을 뺀 나머지로 <see cref="HotkeyPlan.Reconcile"/>을 거쳐
    /// 즉시 재시도해 실제로 등록된 것만 반환한다(재시도는 한 번만). 시작 시점엔 "이전 조합"이라는
    /// 게 없으므로 previous를 빈 맵으로 넘긴다 — 실패한 동작은 되돌릴 곳이 없어 그냥 빠진다.
    /// </summary>
    private (IReadOnlyDictionary<HotkeyAction, HotkeySpec> Applied, IReadOnlyList<HotkeyAction> Failed)
        ApplyDesiredHotkeys(IReadOnlyDictionary<HotkeyAction, HotkeySpec> desired)
    {
        IReadOnlyList<HotkeyAction> failed = _hotkeys.TryApply(desired);
        if (failed.Count == 0) return (desired, failed);

        IReadOnlyDictionary<HotkeyAction, HotkeySpec> plan =
            HotkeyPlan.Reconcile(desired, previous: new Dictionary<HotkeyAction, HotkeySpec>(), failed);
        IReadOnlyList<HotkeyAction> retryFailed = _hotkeys.TryApply(plan);
        IReadOnlyDictionary<HotkeyAction, HotkeySpec> applied = plan;

        if (retryFailed.Count > 0)
        {
            // 방금 성공했던 조합이 다시 실패하는 것은 예상 밖이다 — 루프 없이 결과에 합산만 한다.
            failed = failed.Concat(retryFailed).Distinct().ToList();
            applied = plan.Where(kv => !retryFailed.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        return (applied, failed);
    }

    /// <summary>
    /// 설정 창 OK 경로 전용 등록 로직. <see cref="ApplyDesiredHotkeys"/>와 달리 "이전 조합"이
    /// 존재한다는 전제로, <see cref="HotkeyPlan.Reconcile"/>을 통해 새 조합이 충돌한 동작만
    /// 이전 조합으로 되돌려 한 번 더 시도한다(재시도는 한 번만). 그래도 실패하면 그 동작만
    /// 결과 맵에서 빼고 받아들인다. 반환하는 Unregistered는 desired가 아니라 "실제로 등록에
    /// 실패해 지금 미등록 상태인 모든 동작" — 되돌리기 자체가 성공한 동작은 포함하지 않는다.
    /// </summary>
    private (IReadOnlyDictionary<HotkeyAction, HotkeySpec> Applied, IReadOnlyList<HotkeyAction> Unregistered)
        ApplySettingsHotkeys(
            IReadOnlyDictionary<HotkeyAction, HotkeySpec> previous,
            IReadOnlyDictionary<HotkeyAction, HotkeySpec> desired)
    {
        IReadOnlyList<HotkeyAction> failed = _hotkeys.TryApply(desired);
        IReadOnlyDictionary<HotkeyAction, HotkeySpec> applied = desired;

        if (failed.Count > 0)
        {
            IReadOnlyDictionary<HotkeyAction, HotkeySpec> plan = HotkeyPlan.Reconcile(desired, previous, failed);
            IReadOnlyList<HotkeyAction> retryFailed = _hotkeys.TryApply(plan);
            applied = retryFailed.Count == 0
                ? plan
                : plan.Where(kv => !retryFailed.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        IReadOnlyList<HotkeyAction> unregistered =
            HotkeyActions.All.Where(a => !applied.ContainsKey(a)).ToList();

        return (applied, unregistered);
    }

    /// <summary>동작과 <see cref="HotkeySpec"/> 값 기준으로 두 단축키 맵이 같은지 비교한다.</summary>
    private static bool HotkeysEqual(
        IReadOnlyDictionary<HotkeyAction, HotkeySpec> a,
        IReadOnlyDictionary<HotkeyAction, HotkeySpec> b)
    {
        if (a.Count != b.Count) return false;

        foreach ((HotkeyAction action, HotkeySpec spec) in a)
        {
            if (!b.TryGetValue(action, out HotkeySpec other) || spec != other)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Suspend 후 재개할 때 쓰는 콜백. <see cref="HotkeyWindow.Resume"/>이 돌려주는 실패 목록을
    /// 버리지 않고 알린다 — 캡처 대화상자가 떠 있는 사이 다른 프로그램이 조합을 선점하면
    /// 재시작 전까지 해당 동작이 죽은 채로 남기 때문이다.
    /// </summary>
    private void ResumeHotkeys()
    {
        IReadOnlyList<HotkeyAction> failed = _hotkeys.Resume();
        if (failed.Count == 0) return;

        string names = string.Join(", ", failed.Select(HotkeyActions.DisplayName));
        ShowBalloon("WinDimmer", $"다른 프로그램이 선점해 다시 등록하지 못한 단축키: {names}");
    }

    /// <summary>맵에 해당 동작이 없을 수도 있다는 전제로 안전하게 조회한다.</summary>
    private static string DisplayFor(IReadOnlyDictionary<HotkeyAction, HotkeySpec> hotkeys, HotkeyAction action) =>
        hotkeys.TryGetValue(action, out HotkeySpec spec) ? spec.Display() : "미등록";

    private void UpdateHotkeyMenuText(IReadOnlyDictionary<HotkeyAction, HotkeySpec> hotkeys)
    {
        // hotkeys는 "실제로 등록된" 조합이므로 등록에 실패한 동작이 빠져 있을 수 있다.
        _brightnessMenuItem.Text =
            $"밝기: {DisplayFor(hotkeys, HotkeyAction.Brighter)} / {DisplayFor(hotkeys, HotkeyAction.Darker)}";
        _blackoutMenuItem.Text = $"완전 가림: {DisplayFor(hotkeys, HotkeyAction.Blackout)}";
        _clearAllMenuItem.Text = $"전체 해제: {DisplayFor(hotkeys, HotkeyAction.ClearAll)}";
    }

    /// <summary>
    /// 창 선택을 규칙 생성 목적으로 시작한다. 결과는 콜백으로 돌아온다.
    /// 중첩 메시지 루프를 쓰지 않으므로 모달이 떠 있는 동안 재진입이 열리지 않는다.
    /// </summary>
    public void BeginPickRule(Action<DimRule?> onDone)
    {
        if (_picker.IsActive) { onDone(null); return; }

        _pickPurpose = PickPurpose.Rule;
        _pendingRuleCallback = onDone;
        _picker.Start();

        // Start()는 이미 Dispose된 picker면 Cancelled를 올리지 않고 조용히 반환한다.
        // 이 경우 콜백이 영영 오지 않으므로 여기서 즉시 완료시켜 "콜백은 반드시 한 번 온다"를 지킨다.
        if (!_picker.IsActive) CompleteRulePick(null);
    }

    /// <summary>규칙 생성이 끝났거나 취소됐을 때 한 번만 호출한다.</summary>
    private void CompleteRulePick(DimRule? rule)
    {
        Action<DimRule?>? callback = _pendingRuleCallback;
        _pendingRuleCallback = null;
        _pickPurpose = PickPurpose.Dim;
        callback?.Invoke(rule);
    }

    // 자동 복원 경로(_suppressElevatedBalloon)에서는 조용히 넘어간다 — 사용자가 지금 막
    // 고른 창일 때만("사용자가 지금 관리자 권한 창을 선택했다") 안내가 필요하다.
    private void OnElevatedTarget(DimSession session)
    {
        if (_suppressElevatedBalloon) return;

        ShowBalloon(
            "WinDimmer",
            "대상이 관리자 권한으로 실행 중이라 이벤트 추적이 제한됩니다. " +
            "폴링 모드로 동작하며 약간의 지연이 있을 수 있습니다. " +
            "WinDimmer를 관리자 권한으로 실행하면 해소됩니다.");
    }

    private void WarnAboutInvalidRules()
    {
        if (_watcher.InvalidRules.Count == 0) return;

        string names = string.Join(", ", _watcher.InvalidRules.Select(r => r.ProcessName));
        ShowBalloon("WinDimmer", $"정규식이 잘못된 규칙이 있어 비활성화했습니다: {names}");
    }

    private void UpdateTrayIcon()
    {
        string text = _manager.Count == 0
            ? "WinDimmer — 대상 없음"
            : $"WinDimmer — {_manager.Count}개 디밍 중";

        // NotifyIcon.Text는 63자 제한이 있다.
        _icon.Text = text.Length > 63 ? text[..63] : text;

        _icon.Icon = _manager.Count > 0 ? _activeIcon : _idleIcon;
    }

    private void ShowSettings()
    {
        // 이미 열려 있으면 다른 창에 가려 있을 수 있다 — 조용히 무시하면 사용자가 되찾을 방법이
        // 없다(작업표시줄·Alt-Tab에 없다). 앞으로 가져온다.
        if (_settings is not null) { _settings.Activate(); return; }

        // 설정 창이 열리는 동안 핫키가 suspend되므로 키를 뗐는지 알 수 없다 — 램프를 남겨두면
        // 슬라이더를 만지는 내내 밝기가 제멋대로 흐른다.
        _alphaRamp.Stop();

        byte entryAlpha = Config.DefaultAlpha;

        // MSIX에서는 사용자가 작업 관리자에서 자동 실행을 직접 껐을 수 있어 config가 실제와
        // 어긋날 수 있다 — 체크박스는 실제 StartupTask 상태를 보여준다.
        DimConfig shown = Config;
        if (PackagedApp.IsPackaged)
        {
            try { shown = shown with { AutoStart = PackagedApp.IsAutoStartEnabled() }; }
            catch (Exception) { /* 조회 실패 시 config 값 그대로 보여준다 */ }
        }

        using var form = new SettingsForm(
            shown, SetDefaultAlpha, BeginPickRule, () => IsPickInProgress,
            _hotkeys.Suspend, ResumeHotkeys, _hotkeys.Current, this);
        _settings = form;
        try
        {
            if (form.ShowDialog() != DialogResult.OK)
            {
                SetDefaultAlpha(entryAlpha);   // 취소 — 밝기 되돌리기
                return;
            }

            Config = Config with { Rules = form.Rules };
            SetDefaultAlpha(form.Alpha);   // DefaultAlpha의 유일한 기록자를 거친다
            SetAutoStart(form.AutoStart);   // 내부에서 Config를 갱신한다

            // 설정 경로는 시작 경로(ApplyDesiredHotkeys)와 전제가 다르다: 시작 시엔 "이전 설정"이라는
            // 게 없어서 실패 항목을 뺀 나머지로만 다시 등록해도 되지만, 여기서는 사용자가 이미 쓰고
            // 있던 조합이 있다. 실패 항목을 뺀 채로 재시도하면 TryApply가 먼저 현재 등록을 전부 해제한
            // 뒤 다시 등록하므로, 실패한 동작은 이전 조합조차 잃고 세션 내내 미등록 상태가 된다.
            // 그래서 실패한 동작은 "빼는" 게 아니라 그 동작의 "이전 조합으로 되돌려" 재시도한다.
            // "이전 조합"은 config가 아니라 _hotkeys.Current — 실제로 지금 OS에 등록된 것 — 이어야
            // 한다. config 기준이면 시작 시 등록에 실패한 조합이 마치 살아 있는 것처럼 비교돼,
            // 재시도(=복구) 경로가 영영 열리지 않는다.
            // 또한 바뀐 게 없으면 아예 재등록을 건너뛴다 — 매번 전부 해제 후 재등록하면 그 순간
            // 다른 프로그램이 손대지 않은 조합까지 가로챌 여지가 생긴다.
            IReadOnlyDictionary<HotkeyAction, HotkeySpec> registeredHotkeys = _hotkeys.Current;

            if (!HotkeysEqual(registeredHotkeys, form.Hotkeys))
            {
                (IReadOnlyDictionary<HotkeyAction, HotkeySpec> applied, IReadOnlyList<HotkeyAction> unregistered) =
                    ApplySettingsHotkeys(registeredHotkeys, form.Hotkeys);
                UpdateHotkeyMenuText(applied);

                if (unregistered.Count > 0)
                {
                    string names = string.Join(", ", unregistered.Select(a =>
                        $"{HotkeyActions.DisplayName(a)}({DisplayFor(form.Hotkeys, a)})"));
                    ShowBalloon("WinDimmer",
                        $"다른 프로그램이 사용 중이라 등록하지 못한 단축키: {names}");
                }
            }

            // 등록 결과와 무관하게, config.json에는 사용자가 실제로 요청한 조합(form.Hotkeys)을
            // 그대로 저장한다. 등록된 것(applied)만 저장하면 실패한 동작의 지정이 파일에서 조용히
            // 사라져 다음 실행 때 공장 기본값으로 되돌아간다.
            Config = Config with
            {
                Hotkeys = form.Hotkeys.ToDictionary(kv => HotkeyActions.ConfigKey(kv.Key), kv => kv.Value.Format()),
            };

            // 레지스트리 쓰기 실패 시에도 규칙과 밝기 편집을 저장하려면, SaveConfig()는
            // SetAutoStart의 try/catch 바깥에서만 호출해야 한다.
            // 저장 자체가 실패해도(백신·클라우드 동기화가 파일을 잠근 경우) 여기서 죽으면 안
            // 된다 — 이미 바뀐 in-memory 설정과 워처가 어긋난 채 남는다. 알리고 계속한다.
            try { SaveConfig(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowBalloon("WinDimmer", "설정 파일을 저장하지 못했습니다. 변경 내용은 이번 실행 동안만 유지됩니다.");
                Trace.WriteLine($"설정 저장 실패 — {ex}");
            }
            _watcher.UpdateRules(Config.Rules);
            _watcher.ApplyToExistingWindows();
            WarnAboutInvalidRules();
        }
        finally
        {
            _settings = null;
        }
    }

    private void SetAutoStart(bool enable)
    {
        if (PackagedApp.IsPackaged) { SetAutoStartPackaged(enable); return; }

        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enable) key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(RunValue, throwOnMissingValue: false);

            Config = Config with { AutoStart = enable };
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
        {
            ShowBalloon("WinDimmer", "자동 실행 설정을 변경하지 못했습니다 (레지스트리 접근 거부).");
        }
    }

    // MSIX 패키지에서는 Run 키 쓰기가 가상화되어 실제 자동 실행이 되지 않으므로 StartupTask를 쓴다.
    private void SetAutoStartPackaged(bool enable)
    {
        try
        {
            StartupTaskState state = PackagedApp.SetAutoStart(enable);

            // 켜 달라고 요청해도 앱이 되살릴 수 없는 두 경우는 이유를 알려줘야 한다 —
            // 조용히 있으면 체크박스가 다음에 저절로 풀린 것처럼 보인다.
            switch (state)
            {
                case StartupTaskState.DisabledByUser:
                    ShowBalloon("WinDimmer",
                        "자동 실행이 작업 관리자에서 '사용 안 함'으로 막혀 있습니다. " +
                        "작업 관리자 > 시작 앱에서 WinDimmer를 '사용'으로 바꿔 주세요.");
                    break;
                case StartupTaskState.DisabledByPolicy:
                    ShowBalloon("WinDimmer", "자동 실행이 시스템 정책으로 막혀 있어 켤 수 없습니다.");
                    break;
            }

            // 레지스트리 경로와 달리 요청과 결과가 다를 수 있으므로 config에는 실제 상태를 남긴다.
            Config = Config with
            {
                AutoStart = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy,
            };
        }
        catch (Exception)
        {
            // StartupTask API 실패는 드물지만, 여기서 죽으면 규칙·밝기 저장까지 막힌다.
            ShowBalloon("WinDimmer", "자동 실행 설정을 변경하지 못했습니다.");
        }
    }

    private void CopyDiagnostics()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== WinDimmer 진단 정보 ===");
        sb.AppendLine($"버전: {AppVersion.Display}");
        sb.AppendLine($"설정 경로: {RuleStore.ConfigPath}");
        sb.AppendLine($"기본 알파: {Config.DefaultAlpha}");
        sb.AppendLine($"규칙 수: {Config.Rules.Count}");
        sb.AppendLine($"세션 수: {_manager.Count}");

        using (var p = System.Diagnostics.Process.GetCurrentProcess())
        {
            sb.AppendLine($"프로세스 핸들 수: {p.HandleCount}");
            sb.AppendLine($"누적 CPU: {p.TotalProcessorTime.TotalSeconds:F2}s");
        }

        foreach (DimSession s in _manager.Sessions)
        {
            sb.AppendLine("---");
            sb.AppendLine($"  대상 HWND: 0x{s.Target:X}");
            sb.AppendLine($"  프로세스: {WindowInspector.GetProcessName(s.Target)}");
            sb.AppendLine($"  제목: {WindowInspector.GetTitle(s.Target)}");
            sb.AppendLine($"  훅 등록 완료: {s.HooksRegistered}");
            sb.AppendLine($"  상승 권한 대상: {s.IsElevatedTarget}");
            sb.AppendLine($"  알파: {s.Alpha}{(s.AlphaIsCustom ? " (개별 지정)" : string.Empty)}");
            sb.AppendLine($"  오버레이 HWND: 0x{s.OverlayHandle:X}");
            sb.AppendLine($"  오버레이 바로 아래 창(Z-order): 0x{s.ZNeighbor:X}");

            // TOPMOST 대상은 일반 오버레이가 절대 위로 올라갈 수 없다. 한 줄로 판별된다.
            int exStyle = WindowInspector.GetExStyle(s.Target);
            sb.AppendLine(
                $"  대상 확장 스타일: 0x{exStyle:X8}" +
                ((exStyle & WS_EX_TOPMOST) != 0 ? "  ← TOPMOST" : string.Empty));
            sb.AppendLine($"  대상 소유자: 0x{WindowInspector.GetOwner(s.Target):X}");
            sb.AppendLine("  대상 위쪽 Z-order:");
            foreach (string line in DescribeAbove(s.Target, s.OverlayHandle)) sb.AppendLine($"    {line}");
        }

        sb.AppendLine("=== 이벤트 로그 ===");
        sb.AppendLine(DiagLog.Dump());

        static IEnumerable<string> DescribeAbove(IntPtr target, IntPtr overlay)
        {
            IntPtr current = target;
            for (int i = 0; i < 8; i++)
            {
                current = User32.GetWindow(current, GW_HWNDPREV);
                if (current == IntPtr.Zero)
                {
                    yield return "(맨 위)";
                    yield break;
                }

                string mark = current == overlay ? "  ← 오버레이" : string.Empty;
                yield return
                    $"0x{current:X} owner=0x{WindowInspector.GetOwner(current):X} " +
                    $"vis={User32.IsWindowVisible(current)} " +
                    $"ex=0x{WindowInspector.GetExStyle(current):X8} " +
                    $"proc={WindowInspector.GetProcessName(current)} " +
                    $"title='{WindowInspector.GetTitle(current)}'{mark}";
            }
            yield return "…";
        }

        try
        {
            Clipboard.SetText(sb.ToString());
            ShowBalloon("WinDimmer", "진단 정보를 클립보드에 복사했습니다.");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            ShowBalloon("WinDimmer", "다른 프로그램이 클립보드를 사용 중이라 복사하지 못했습니다.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 핫키·설정 창으로 바꾼 밝기는 여기서만 저장된다. 조작마다 매번 저장하면 과도하다
        try { SaveConfig(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine($"종료 시 설정 저장 실패 — {ex}");
        }

        _alphaRamp.Dispose();
        _manager.SessionsChanged -= UpdateTrayIcon;
        _manager.ElevatedTargetDetected -= OnElevatedTarget;
        _picker.Picked -= OnPicked;
        _picker.Cancelled -= OnPickerCancelled;
        _picker.Dispose();
        _watcher.Dispose();
        _hotkeys.Pressed -= OnHotkeyPressed;
        _hotkeys.Dispose();
        _icon.Visible = false;   // 이걸 빠뜨리면 종료 후 유령 아이콘이 남는다
        _icon.Dispose();
        // NotifyIcon이 더 이상 참조하지 않게 된 뒤에 캐시한 아이콘을 해제한다
        _idleIcon.Dispose();
        _activeIcon.Dispose();
        _menu.Dispose();
    }
}
