using WinDimmer;
using Xunit;

public class AlphaMathTests
{
    [Theory]
    [InlineData(110, 10, 120)]
    [InlineData(110, -10, 100)]
    [InlineData(250, 10, 255)]   // 상한 클램프
    [InlineData(5, -10, 0)]      // 하한 클램프
    [InlineData(0, -10, 0)]
    [InlineData(255, 10, 255)]
    public void Adjust_clamps_to_byte_range(byte current, int delta, byte expected)
    {
        Assert.Equal(expected, AlphaMath.Adjust(current, delta));
    }

    [Fact]
    public void Defaults_match_spec()
    {
        Assert.Equal(110, AlphaMath.Default);
        Assert.Equal(10, AlphaMath.Step);
    }
}
