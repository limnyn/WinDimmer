using System.Text.Json;

namespace WinDimmer;

/// <summary>config.json을 원자적으로 읽고 쓴다.</summary>
public static class RuleStore
{
    public static string ConfigPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WinDimmer", "config.json");

    public static DimConfig Load() => Load(ConfigPath);

    public static DimConfig Load(string path)
    {
        if (!File.Exists(path)) return new DimConfig();

        try
        {
            string json = File.ReadAllText(path);
            DimConfig? loaded = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.DimConfig);
            if (loaded is null) return new DimConfig();

            // init 전용 속성은 소스 생성기가 객체 이니셜라이저로 구성하므로,
            // JSON에 없는 컬렉션 속성은 필드 기본값(new()) 대신 null이 된다.
            // 이전 버전 설정 파일(예: hotkeys 섹션이 없는 파일) 호환을 위해 보정한다.
            return loaded with
            {
                Rules = loaded.Rules ?? new(),
                Hotkeys = loaded.Hotkeys ?? new(),
                AutoDimProcesses = loaded.AutoDimProcesses ?? new(),
            };
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            BackupCorrupt(path);
            return new DimConfig();
        }
    }

    public static void Save(DimConfig config) => Save(config, ConfigPath);

    public static void Save(DimConfig config, string path)
    {
        string dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        // 임시 파일에 먼저 쓴 뒤 교체한다. 중간에 종료돼도 설정이 깨지지 않는다.
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, ConfigJsonContext.Default.DimConfig));

        if (File.Exists(path)) File.Replace(temp, path, destinationBackupFileName: null);
        else File.Move(temp, path);
    }

    /// <summary>
    /// 설정의 단축키를 해석한다. 항목이 없거나 해석에 실패하면 그 동작만 기본값으로 대체한다.
    /// 손상된 단축키 하나 때문에 규칙 전부를 잃는 것은 과한 처벌이다.
    /// </summary>
    public static IReadOnlyDictionary<HotkeyAction, HotkeySpec> ResolveHotkeys(DimConfig config)
    {
        // 현재(camelCase, "toggle") 형식과 예전(PascalCase, "Toggle") 형식을 모두 읽을 수 있도록
        // 대소문자를 구분하지 않는 조회 테이블을 만든다.
        var lookup = new Dictionary<string, string>(config.Hotkeys, StringComparer.OrdinalIgnoreCase);
        var resolved = new Dictionary<HotkeyAction, HotkeySpec>();

        foreach (HotkeyAction action in HotkeyActions.All)
        {
            if (lookup.TryGetValue(HotkeyActions.ConfigKey(action), out string? text)
                && HotkeySpec.TryParse(text, out HotkeySpec spec))
            {
                resolved[action] = spec;
            }
            else
            {
                resolved[action] = HotkeyActions.Default(action);
            }
        }

        return resolved;
    }

    private static void BackupCorrupt(string path)
    {
        try
        {
            if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
