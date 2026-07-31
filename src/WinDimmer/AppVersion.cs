using System.Reflection;

namespace WinDimmer;

/// <summary>어셈블리에 구워진 버전·빌드 일자. 트레이 메뉴, 설정 창, 진단 정보에 표시한다.</summary>
public static class AppVersion
{
    /// <summary>예: "1.0.0 (2026-07-31)". 빌드 일자 메타데이터가 없으면 버전만.</summary>
    public static string Display { get; } = Compose(
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildDate")?.Value);

    /// <summary>
    /// 표시 문자열 조립. InformationalVersion에는 SDK가 git 커밋 해시를 덧붙일 수 있어
    /// ("1.0.0+abc123") 화면용으로는 '+' 뒤를 잘라낸다.
    /// </summary>
    public static string Compose(string? informationalVersion, string? buildDate)
    {
        string version = string.IsNullOrWhiteSpace(informationalVersion) ? "?" : informationalVersion;
        int plus = version.IndexOf('+');
        if (plus >= 0) version = version[..plus];

        return string.IsNullOrWhiteSpace(buildDate) ? version : $"{version} ({buildDate})";
    }
}
