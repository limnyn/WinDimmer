using WinDimmer;
using Xunit;

public class AutoDimListTests
{
    [Fact]
    public void Add_adds_a_new_name()
    {
        IReadOnlyList<string> result = AutoDimList.Add(Array.Empty<string>(), "notepad");

        Assert.Equal(new[] { "notepad" }, result);
    }

    [Fact]
    public void Add_ignores_a_duplicate_in_different_case()
    {
        IReadOnlyList<string> result = AutoDimList.Add(new[] { "notepad" }, "NotePad");

        Assert.Equal(new[] { "notepad" }, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Add_ignores_blank_or_whitespace_only_names(string blank)
    {
        IReadOnlyList<string> result = AutoDimList.Add(new[] { "notepad" }, blank);

        Assert.Equal(new[] { "notepad" }, result);
    }

    [Fact]
    public void Add_trims_the_name()
    {
        IReadOnlyList<string> result = AutoDimList.Add(Array.Empty<string>(), "  notepad  ");

        Assert.Equal(new[] { "notepad" }, result);
    }

    [Fact]
    public void Add_on_a_non_empty_list_appends_rather_than_replaces()
    {
        IReadOnlyList<string> result = AutoDimList.Add(new[] { "notepad" }, "chrome");

        Assert.Equal(new[] { "notepad", "chrome" }, result);
    }

    [Fact]
    public void Remove_of_an_absent_name_is_a_no_op()
    {
        IReadOnlyList<string> original = new[] { "notepad" };

        IReadOnlyList<string> result = AutoDimList.Remove(original, "chrome");

        Assert.Equal(new[] { "notepad" }, result);
    }

    [Fact]
    public void Remove_is_case_insensitive()
    {
        IReadOnlyList<string> result = AutoDimList.Remove(new[] { "notepad" }, "NOTEPAD");

        Assert.Empty(result);
    }

    [Fact]
    public void Contains_is_case_insensitive()
    {
        Assert.True(AutoDimList.Contains(new[] { "notepad" }, "NotePad"));
        Assert.False(AutoDimList.Contains(new[] { "notepad" }, "chrome"));
    }

    [Fact]
    public void Contains_and_Remove_trim_a_hand_edited_entry_with_surrounding_whitespace()
    {
        IReadOnlyList<string> list = new[] { " notepad " };

        Assert.True(AutoDimList.Contains(list, "notepad"));

        IReadOnlyList<string> result = AutoDimList.Remove(list, "notepad");

        Assert.Empty(result);
    }

    [Fact]
    public void Add_never_mutates_the_input_list()
    {
        var original = new List<string> { "notepad" };

        _ = AutoDimList.Add(original, "chrome");

        Assert.Equal(new[] { "notepad" }, original);
    }

    [Fact]
    public void Remove_never_mutates_the_input_list()
    {
        var original = new List<string> { "notepad" };

        _ = AutoDimList.Remove(original, "notepad");

        Assert.Equal(new[] { "notepad" }, original);
    }
}
