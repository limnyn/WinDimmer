using WinDimmer;
using Xunit;

public class RuleRowMapperTests
{
    [Fact]
    public void Normal_row_maps_correctly()
    {
        var rows = new[] { new RuleRowValues("notepad", "메모장$", 120, true) };

        RuleRowMapResult result = RuleRowMapper.Map(rows);

        Assert.True(result.IsSuccess);
        DimRule rule = Assert.Single(result.Rules);
        Assert.Equal("notepad", rule.ProcessName);
        Assert.Equal("메모장$", rule.TitlePattern);
        Assert.Equal(120, rule.Alpha);
        Assert.True(rule.Enabled);
    }

    [Fact]
    public void Blank_and_whitespace_process_names_are_skipped()
    {
        var rows = new[]
        {
            new RuleRowValues("", "", 100, true),
            new RuleRowValues("   ", "", 100, true),
            new RuleRowValues("chrome", "", 100, true),
        };

        RuleRowMapResult result = RuleRowMapper.Map(rows);

        Assert.True(result.IsSuccess);
        DimRule rule = Assert.Single(result.Rules);
        Assert.Equal("chrome", rule.ProcessName);
    }

    [Fact]
    public void Process_name_is_trimmed()
    {
        var rows = new[] { new RuleRowValues("  notepad  ", "", 100, true) };

        RuleRowMapResult result = RuleRowMapper.Map(rows);

        Assert.Equal("notepad", result.Rules[0].ProcessName);
    }

    [Fact]
    public void Brightness_clamps_at_lower_bound()
    {
        var rows = new[] { new RuleRowValues("notepad", "", -50, true) };

        RuleRowMapResult result = RuleRowMapper.Map(rows);

        Assert.Equal(0, result.Rules[0].Alpha);
    }

    [Fact]
    public void Brightness_clamps_at_upper_bound()
    {
        var rows = new[] { new RuleRowValues("notepad", "", 999, true) };

        RuleRowMapResult result = RuleRowMapper.Map(rows);

        Assert.Equal(255, result.Rules[0].Alpha);
    }

    [Fact]
    public void Empty_title_pattern_is_valid()
    {
        var rows = new[] { new RuleRowValues("notepad", "", 100, true) };

        RuleRowMapResult result = RuleRowMapper.Map(rows);

        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, result.Rules[0].TitlePattern);
    }

    [Fact]
    public void Invalid_pattern_is_reported_and_identifies_offending_pattern()
    {
        var rows = new[] { new RuleRowValues("notepad", "[unclosed", 100, true) };

        RuleRowMapResult result = RuleRowMapper.Map(rows);

        Assert.False(result.IsSuccess);
        Assert.Equal("[unclosed", result.InvalidPattern);
    }

    [Fact]
    public void Valid_pattern_passes()
    {
        var rows = new[] { new RuleRowValues("notepad", "메모장$", 100, true) };

        RuleRowMapResult result = RuleRowMapper.Map(rows);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Row_order_is_preserved()
    {
        var rows = new[]
        {
            new RuleRowValues("a", "", 1, true),
            new RuleRowValues("b", "", 2, true),
            new RuleRowValues("c", "", 3, true),
        };

        RuleRowMapResult result = RuleRowMapper.Map(rows);

        Assert.Equal(new[] { "a", "b", "c" }, result.Rules.Select(r => r.ProcessName));
    }

    [Fact]
    public void Enabled_flag_is_carried_through()
    {
        var rows = new[] { new RuleRowValues("notepad", "", 100, false) };

        RuleRowMapResult result = RuleRowMapper.Map(rows);

        Assert.False(result.Rules[0].Enabled);
    }
}
