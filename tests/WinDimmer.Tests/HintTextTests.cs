using WinDimmer;
using Xunit;

public class HintTextTests
{
    [Fact]
    public void Describe_with_process_name_and_title()
    {
        Assert.Equal("notepad — 제목 없음 - 메모장", HintText.Describe("notepad", "제목 없음 - 메모장"));
    }

    [Fact]
    public void Describe_with_process_name_and_empty_title()
    {
        Assert.Equal("notepad", HintText.Describe("notepad", ""));
    }

    [Fact]
    public void Describe_with_empty_process_name_and_nonempty_title()
    {
        Assert.Equal(
            "(프로세스 이름을 읽을 수 없음 — 선택은 가능) — 제목",
            HintText.Describe("", "제목"));
    }

    [Fact]
    public void Describe_with_both_empty_reports_unselectable_window()
    {
        Assert.Equal("(선택할 수 없는 창)", HintText.Describe("", ""));
    }

    [Fact]
    public void Long_title_is_truncated_with_ellipsis_and_never_exceeds_limit()
    {
        string longTitle = new string('가', HintText.MaxTitleLength + 20);
        string result = HintText.Describe("chrome", longTitle);

        Assert.Contains("…", result);
        string titlePart = result["chrome — ".Length..];
        Assert.True(titlePart.Length <= HintText.MaxTitleLength);
    }

    [Fact]
    public void Title_exactly_at_limit_is_untouched()
    {
        string title = new string('a', HintText.MaxTitleLength);
        Assert.Equal(title, HintText.Ellipsize(title, HintText.MaxTitleLength));
    }

    [Fact]
    public void Ellipsize_with_tiny_max_does_not_throw()
    {
        var ex = Record.Exception(() => HintText.Ellipsize("hello world", 0));
        Assert.Null(ex);

        ex = Record.Exception(() => HintText.Ellipsize("hello world", 1));
        Assert.Null(ex);
    }
}
