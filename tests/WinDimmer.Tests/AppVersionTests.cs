using WinDimmer;
using Xunit;

public class AppVersionTests
{
    [Theory]
    [InlineData("1.0.0", "2026-07-31", "1.0.0 (2026-07-31)")]
    [InlineData("1.0.0+abc1234", "2026-07-31", "1.0.0 (2026-07-31)")]   // SDK가 붙인 커밋 해시는 잘린다
    [InlineData("1.0.0", null, "1.0.0")]
    [InlineData("1.0.0+abc1234", "", "1.0.0")]
    [InlineData(null, "2026-07-31", "? (2026-07-31)")]                  // 속성이 없어도 죽지 않는다
    [InlineData("", null, "?")]
    public void Compose_formats_version_and_date(string? version, string? date, string expected)
    {
        Assert.Equal(expected, AppVersion.Compose(version, date));
    }
}
