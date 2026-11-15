using QuickCommands.Core;

namespace QuickCommands.Tests;

public class ProfileBlockTests
{
    [Fact]
    public void Upsert_into_empty_file_creates_only_the_block()
    {
        var result = ProfileBlock.Upsert("", "load-me", "\n");
        Assert.Equal($"{ProfileBlock.Begin}\nload-me\n{ProfileBlock.End}\n", result);
    }

    [Fact]
    public void Upsert_appends_after_a_blank_line_and_keeps_existing_content()
    {
        var result = ProfileBlock.Upsert("function open { explorer . }\n", "load-me", "\n");
        Assert.Equal($"function open {{ explorer . }}\n\n{ProfileBlock.Begin}\nload-me\n{ProfileBlock.End}\n", result);
    }

    [Fact]
    public void Upsert_twice_replaces_the_block_instead_of_duplicating_it()
    {
        var once = ProfileBlock.Upsert("a\n", "old", "\n");
        var twice = ProfileBlock.Upsert(once + "b\n", "new", "\n");

        Assert.Single(twice.Split(ProfileBlock.Begin)[1..]);
        Assert.Contains("new", twice);
        Assert.DoesNotContain("old", twice);
        Assert.EndsWith("b\n", twice);
    }

    [Fact]
    public void Upsert_keeps_crlf_line_endings_of_the_file()
    {
        var result = ProfileBlock.Upsert("a\r\nb\r\n", "x", "\n");
        Assert.DoesNotContain("\n", result.Replace("\r\n", ""));
    }

    [Theory]
    [InlineData("")]
    [InlineData("function open { explorer . }\n")]
    [InlineData("a\r\nb\r\n")]
    [InlineData("fnm env --use-on-cd | Out-String | Invoke-Expression\nfnm use 22\n")]
    public void Remove_restores_the_original_content(string original)
    {
        var installed = ProfileBlock.Upsert(original, "load-me", "\r\n");
        Assert.True(ProfileBlock.Contains(installed));
        Assert.Equal(original, ProfileBlock.Remove(installed));
    }

    [Fact]
    public void Remove_without_block_changes_nothing()
    {
        Assert.Equal("a\n\nb\n", ProfileBlock.Remove("a\n\nb\n"));
    }
}
