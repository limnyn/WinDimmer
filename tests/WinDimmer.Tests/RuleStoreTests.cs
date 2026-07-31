using WinDimmer;
using Xunit;

public class RuleStoreTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "WinDimmerTests", Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_dir, "config.json");

    public RuleStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void Missing_file_yields_defaults()
    {
        DimConfig c = RuleStore.Load(Path_);
        Assert.Equal(1, c.Version);
        Assert.Equal(AlphaMath.Default, c.DefaultAlpha);
        Assert.False(c.AutoStart);
        Assert.Empty(c.Rules);
    }

    [Fact]
    public void Round_trips_a_config()
    {
        var original = new DimConfig
        {
            DefaultAlpha = 200,
            AutoStart = true,
            Rules = { new DimRule { ProcessName = "notepad", TitlePattern = "메모장$", Alpha = 90 } },
        };

        RuleStore.Save(original, Path_);
        DimConfig loaded = RuleStore.Load(Path_);

        Assert.Equal(1, loaded.Version);
        Assert.Equal(200, loaded.DefaultAlpha);
        Assert.True(loaded.AutoStart);
        Assert.Single(loaded.Rules);
        Assert.Equal("notepad", loaded.Rules[0].ProcessName);
        Assert.Equal("메모장$", loaded.Rules[0].TitlePattern);
        Assert.Equal(90, loaded.Rules[0].Alpha);
        Assert.True(loaded.Rules[0].Enabled);
    }

    [Fact]
    public void Uses_camel_case_on_disk()
    {
        RuleStore.Save(new DimConfig { DefaultAlpha = 111 }, Path_);
        string json = File.ReadAllText(Path_);
        Assert.Contains("\"defaultAlpha\"", json);
        Assert.DoesNotContain("\"DefaultAlpha\"", json);
    }

    [Fact]
    public void Corrupt_file_is_backed_up_and_defaults_returned()
    {
        File.WriteAllText(Path_, "{ this is not json");

        DimConfig c = RuleStore.Load(Path_);

        Assert.Empty(c.Rules);
        Assert.Equal(AlphaMath.Default, c.DefaultAlpha);
        Assert.True(File.Exists(Path_ + ".bak"), "손상 파일이 .bak으로 보존되어야 한다");
    }

    [Fact]
    public void Save_overwrites_existing_file()
    {
        RuleStore.Save(new DimConfig { DefaultAlpha = 50 }, Path_);
        RuleStore.Save(new DimConfig { DefaultAlpha = 60 }, Path_);
        Assert.Equal(60, RuleStore.Load(Path_).DefaultAlpha);
    }

    [Fact]
    public void Save_creates_missing_directory()
    {
        string nested = System.IO.Path.Combine(_dir, "a", "b", "config.json");
        RuleStore.Save(new DimConfig { DefaultAlpha = 70 }, nested);
        Assert.Equal(70, RuleStore.Load(nested).DefaultAlpha);
    }

    [Fact]
    public void Save_leaves_no_temp_file_behind()
    {
        // 첫 저장은 File.Move 경로(대상 없음)를, 두 번째 저장은
        // 크래시 안전성의 핵심인 File.Replace 경로(대상 있음)를 탄다.
        RuleStore.Save(new DimConfig(), Path_);
        RuleStore.Save(new DimConfig { DefaultAlpha = 1 }, Path_);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Hotkeys_round_trip()
    {
        var original = new DimConfig
        {
            Hotkeys = new Dictionary<string, string>
            {
                ["Toggle"] = "Ctrl+Shift+D",
                ["ClearAll"] = "Ctrl+Alt+Z",
            },
        };

        RuleStore.Save(original, Path_);
        DimConfig loaded = RuleStore.Load(Path_);

        Assert.Equal("Ctrl+Shift+D", loaded.Hotkeys["Toggle"]);
        Assert.Equal("Ctrl+Alt+Z", loaded.Hotkeys["ClearAll"]);
    }

    [Fact]
    public void Config_without_a_hotkeys_section_still_loads()
    {
        // 단축키 기능 이전에 저장된 설정 파일
        File.WriteAllText(Path_, """
            { "version": 1, "defaultAlpha": 90, "autoStart": false, "rules": [] }
            """);

        DimConfig loaded = RuleStore.Load(Path_);

        Assert.Equal(90, loaded.DefaultAlpha);
        Assert.Empty(loaded.Hotkeys);
    }

    [Fact]
    public void Resolve_fills_every_action_from_defaults_when_nothing_is_configured()
    {
        var resolved = RuleStore.ResolveHotkeys(new DimConfig());

        Assert.Equal(HotkeyActions.All.Count, resolved.Count);
        foreach (HotkeyAction action in HotkeyActions.All)
            Assert.Equal(HotkeyActions.Default(action), resolved[action]);
    }

    [Fact]
    public void Resolve_uses_the_configured_combination_when_present()
    {
        var config = new DimConfig
        {
            Hotkeys = new Dictionary<string, string> { ["Toggle"] = "Ctrl+Shift+D" },
        };

        var resolved = RuleStore.ResolveHotkeys(config);

        Assert.Equal("Ctrl+Shift+D", resolved[HotkeyAction.Toggle].Format());
        // 나머지는 기본값
        Assert.Equal(HotkeyActions.Default(HotkeyAction.Darker), resolved[HotkeyAction.Darker]);
    }

    [Theory]
    [InlineData("쓰레기")]
    [InlineData("D")]            // 수정키 없음 — 무효
    [InlineData("")]
    public void Resolve_falls_back_per_action_for_a_corrupt_entry(string bad)
    {
        var config = new DimConfig
        {
            Hotkeys = new Dictionary<string, string>
            {
                ["Toggle"] = bad,
                ["ClearAll"] = "Ctrl+Alt+Z",
            },
            Rules = { new DimRule { ProcessName = "notepad" } },
        };

        var resolved = RuleStore.ResolveHotkeys(config);

        // 손상된 항목만 기본값으로 대체된다
        Assert.Equal(HotkeyActions.Default(HotkeyAction.Toggle), resolved[HotkeyAction.Toggle]);
        // 멀쩡한 항목은 유지된다
        Assert.Equal("Ctrl+Alt+Z", resolved[HotkeyAction.ClearAll].Format());
        // 규칙은 손상된 단축키와 무관하게 보존된다
        Assert.Single(config.Rules);
    }

    [Fact]
    public void Resolve_ignores_an_unknown_action_name()
    {
        var config = new DimConfig
        {
            Hotkeys = new Dictionary<string, string> { ["없는동작"] = "Ctrl+Alt+Q" },
        };

        var resolved = RuleStore.ResolveHotkeys(config);

        Assert.Equal(HotkeyActions.All.Count, resolved.Count);
    }

    [Fact]
    public void Resolve_reads_the_documented_camel_case_keys()
    {
        // 설계 문서(§4)가 약속한 형식: "toggle", "clearAll" — 사용자가 손으로 파일을 편집할 때 쓰는 이름.
        var config = new DimConfig
        {
            Hotkeys = new Dictionary<string, string>
            {
                ["toggle"] = "Ctrl+Shift+D",
                ["clearAll"] = "Ctrl+Alt+Z",
            },
        };

        var resolved = RuleStore.ResolveHotkeys(config);

        Assert.Equal("Ctrl+Shift+D", resolved[HotkeyAction.Toggle].Format());
        Assert.Equal("Ctrl+Alt+Z", resolved[HotkeyAction.ClearAll].Format());
    }

    [Fact]
    public void Resolve_still_accepts_legacy_pascal_case_keys()
    {
        // 예전에 kv.Key.ToString()으로 저장된 파일("Toggle")도 계속 열려야 한다.
        var config = new DimConfig
        {
            Hotkeys = new Dictionary<string, string> { ["Toggle"] = "Ctrl+Shift+D" },
        };

        var resolved = RuleStore.ResolveHotkeys(config);

        Assert.Equal("Ctrl+Shift+D", resolved[HotkeyAction.Toggle].Format());
    }

    [Fact]
    public void Save_writes_camel_case_hotkey_keys_to_disk()
    {
        var config = new DimConfig
        {
            Hotkeys = new Dictionary<string, string>
            {
                [HotkeyActions.ConfigKey(HotkeyAction.Toggle)] = "Ctrl+Alt+D",
                [HotkeyActions.ConfigKey(HotkeyAction.ClearAll)] = "Ctrl+Alt+X",
            },
        };

        RuleStore.Save(config, Path_);
        string json = File.ReadAllText(Path_);

        Assert.Contains("\"toggle\"", json);
        Assert.Contains("\"clearAll\"", json);
        Assert.DoesNotContain("\"Toggle\"", json);
        Assert.DoesNotContain("\"ClearAll\"", json);
    }

    [Fact]
    public void Config_without_a_rules_key_still_loads_with_an_empty_rules_list()
    {
        // Rules = loaded.Rules ?? new() 가드가 실제로 지키는 경로 — 이게 없으면
        // TrayApp 생성자의 _watcher.UpdateRules(null)에서 시작 자체가 죽는다.
        File.WriteAllText(Path_, """
            { "version": 1, "defaultAlpha": 90, "autoStart": false }
            """);

        DimConfig loaded = RuleStore.Load(Path_);

        Assert.NotNull(loaded.Rules);
        Assert.Empty(loaded.Rules);
    }

    [Fact]
    public void Config_without_an_auto_dim_processes_key_still_loads_with_an_empty_list()
    {
        // autoDimProcesses 도입 이전에 저장된 설정 파일 — 가드가 없으면
        // TrayApp 생성자의 _watcher.UpdateAutoDimProcesses(null)에서 시작 자체가 죽는다.
        File.WriteAllText(Path_, """
            { "version": 1, "defaultAlpha": 90, "autoStart": false }
            """);

        DimConfig loaded = RuleStore.Load(Path_);

        Assert.NotNull(loaded.AutoDimProcesses);
        Assert.Empty(loaded.AutoDimProcesses);
    }

    [Fact]
    public void Auto_dim_processes_round_trip()
    {
        var original = new DimConfig { AutoDimProcesses = { "notepad", "chrome" } };

        RuleStore.Save(original, Path_);
        DimConfig loaded = RuleStore.Load(Path_);

        Assert.Equal(new[] { "notepad", "chrome" }, loaded.AutoDimProcesses);
    }
}
