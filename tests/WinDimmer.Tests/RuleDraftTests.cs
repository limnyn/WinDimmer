using WinDimmer;
using Xunit;

public class RuleDraftTests
{
    [Fact]
    public void Builds_a_rule_from_a_picked_window()
    {
        DimRule? rule = RuleDraft.FromWindow("notepad", 120);

        Assert.NotNull(rule);
        Assert.Equal("notepad", rule!.ProcessName);
        Assert.Equal(120, rule.Alpha);
        Assert.True(rule.Enabled);
    }

    [Fact]
    public void Title_pattern_starts_empty_so_the_rule_matches_every_window_of_that_process()
    {
        DimRule? rule = RuleDraft.FromWindow("notepad", 110);
        Assert.Equal(string.Empty, rule!.TitlePattern);
    }

    [Fact]
    public void Trims_surrounding_whitespace_from_the_process_name()
    {
        DimRule? rule = RuleDraft.FromWindow("  notepad  ", 110);
        Assert.Equal("notepad", rule!.ProcessName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Returns_null_when_the_process_name_is_unusable(string processName)
    {
        Assert.Null(RuleDraft.FromWindow(processName, 110));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    public void Carries_the_given_alpha_through_unchanged(byte alpha)
    {
        Assert.Equal(alpha, RuleDraft.FromWindow("notepad", alpha)!.Alpha);
    }
}
