using WinDimmer;
using Xunit;

public class RuleMatcherTests
{
    private static DimRule Rule(string proc, string pattern, bool enabled = true) =>
        new() { ProcessName = proc, TitlePattern = pattern, Alpha = 120, Enabled = enabled };

    [Fact]
    public void Matches_on_process_name_and_title_pattern()
    {
        var m = new RuleMatcher(new[] { Rule("notepad", "메모장$") });
        DimRule? hit = m.Match("notepad", "제목 없음 - 메모장");
        Assert.NotNull(hit);
        Assert.Equal(120, hit!.Alpha);
    }

    [Fact]
    public void Process_name_comparison_ignores_case()
    {
        var m = new RuleMatcher(new[] { Rule("NotePad", "메모장$") });
        Assert.NotNull(m.Match("notepad", "제목 없음 - 메모장"));
    }

    [Fact]
    public void Does_not_match_when_process_differs()
    {
        var m = new RuleMatcher(new[] { Rule("notepad", "메모장$") });
        Assert.Null(m.Match("chrome", "제목 없음 - 메모장"));
    }

    [Fact]
    public void Does_not_match_when_title_pattern_fails()
    {
        var m = new RuleMatcher(new[] { Rule("notepad", "^보고서") });
        Assert.Null(m.Match("notepad", "제목 없음 - 메모장"));
    }

    [Fact]
    public void Empty_pattern_matches_any_title()
    {
        var m = new RuleMatcher(new[] { Rule("notepad", "") });
        Assert.NotNull(m.Match("notepad", "아무 제목"));
    }

    [Fact]
    public void Disabled_rules_never_match()
    {
        var m = new RuleMatcher(new[] { Rule("notepad", "메모장$", enabled: false) });
        Assert.Null(m.Match("notepad", "제목 없음 - 메모장"));
    }

    [Fact]
    public void Invalid_pattern_is_reported_and_never_matches()
    {
        var bad = Rule("notepad", "[unclosed");
        var m = new RuleMatcher(new[] { bad });

        Assert.Null(m.Match("notepad", "아무 제목"));
        Assert.Single(m.InvalidRules);
        Assert.Equal("[unclosed", m.InvalidRules[0].TitlePattern);
    }

    [Fact]
    public void Invalid_pattern_does_not_block_other_rules()
    {
        var m = new RuleMatcher(new[] { Rule("notepad", "[unclosed"), Rule("chrome", "") });
        Assert.NotNull(m.Match("chrome", "아무 제목"));
    }

    [Fact]
    public void First_matching_rule_wins()
    {
        var first = new DimRule { ProcessName = "notepad", TitlePattern = "", Alpha = 50 };
        var second = new DimRule { ProcessName = "notepad", TitlePattern = "", Alpha = 200 };
        var m = new RuleMatcher(new[] { first, second });
        Assert.Equal(50, m.Match("notepad", "제목")!.Alpha);
    }

    [Fact]
    public void HasRules_is_false_when_rule_set_is_empty()
    {
        var m = new RuleMatcher(Array.Empty<DimRule>());
        Assert.False(m.HasRules);
    }

    [Fact]
    public void HasRules_is_false_when_all_rules_are_disabled()
    {
        var m = new RuleMatcher(new[] { Rule("notepad", "", enabled: false) });
        Assert.False(m.HasRules);
    }

    [Fact]
    public void HasRules_is_true_when_an_enabled_rule_exists()
    {
        var m = new RuleMatcher(new[] { Rule("notepad", "") });
        Assert.True(m.HasRules);
    }

    [Fact]
    public void IsValidPattern_accepts_a_valid_pattern()
    {
        Assert.True(RuleMatcher.IsValidPattern("메모장$"));
    }

    [Fact]
    public void IsValidPattern_accepts_the_empty_pattern()
    {
        Assert.True(RuleMatcher.IsValidPattern(""));
    }

    [Fact]
    public void IsValidPattern_rejects_an_unclosed_pattern()
    {
        Assert.False(RuleMatcher.IsValidPattern("[unclosed"));
    }

    [Fact]
    public void Catastrophic_pattern_times_out_instead_of_hanging()
    {
        // 파국적 백트래킹을 유발하는 패턴. 타임아웃이 없으면 UI 스레드가 멈춘다.
        var m = new RuleMatcher(new[] { Rule("notepad", "^(a+)+$") });
        string evil = new string('a', 40) + "!";

        var sw = System.Diagnostics.Stopwatch.StartNew();
        DimRule? hit = m.Match("notepad", evil);
        sw.Stop();

        Assert.Null(hit);
        Assert.True(sw.ElapsedMilliseconds < 2000,
            $"매칭이 {sw.ElapsedMilliseconds}ms 걸렸다. 타임아웃이 동작하지 않는다.");
    }
}
