using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinDimmer;

public sealed record DimRule
{
    public string ProcessName { get; init; } = string.Empty;
    public string TitlePattern { get; init; } = string.Empty;
    public byte Alpha { get; init; } = AlphaMath.Default;
    public bool Enabled { get; init; } = true;
}

public sealed record DimConfig
{
    /// <summary>현재는 쓰지 않지만 향후 스키마 변경 시 마이그레이션 분기점이 된다.</summary>
    public int Version { get; init; } = 1;

    public byte DefaultAlpha { get; init; } = AlphaMath.Default;
    public bool AutoStart { get; init; }
    public List<DimRule> Rules { get; init; } = new();

    /// <summary>사용자가 직접 디밍한 프로세스 이름. 재시작 후 자동 복원에 쓴다.</summary>
    public List<string> AutoDimProcesses { get; init; } = new();

    /// <summary>동작 이름 → "Ctrl+Alt+D" 형식 문자열. 비어 있으면 전부 기본값을 쓴다.</summary>
    public Dictionary<string, string> Hotkeys { get; init; } = new();
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DimConfig))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext;
