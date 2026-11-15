using QuickCommands.Server.Data;
using QuickCommands.Server.Validators;

namespace QuickCommands.Server.Tests.Unit;

public sealed class StoreRulesTests
{
    [Theory]
    [InlineData(" Dev Tools ", "dev-tools")]
    [InlineData("GIT", "git")]
    [InlineData("multi__under score", "multi-under-score")]
    [InlineData("-trim-", "trim")]
    public void Tags_are_normalized(string input, string expected) => Assert.Equal(expected, StoreRules.NormalizeTag(input));

    [Fact]
    public void Tag_list_is_deduplicated_after_normalizing() =>
        Assert.Equal(["git", "dev-tools"], StoreRules.NormalizeTags(["Git", "git ", "Dev Tools", "  "]));

    [Theory]
    [InlineData("a", false)]
    [InlineData("ab", true)]
    [InlineData("c#", false)]
    [InlineData("abcdefghijklmnopqrstuvwxy", false)]
    public void Tag_validity(string tag, bool valid) => Assert.Equal(valid, StoreRules.IsValidTag(tag));

    [Theory]
    [InlineData("mkcd", true)]
    [InlineData("git-st_2", true)]
    [InlineData("qc", false)]
    [InlineData("_x", false)]
    [InlineData("", false)]
    [InlineData("a b", false)]
    public void Command_names_follow_the_desktop_rules(string name, bool valid) => Assert.Equal(valid, StoreRules.IsValidCommandName(name));

    [Theory]
    [InlineData("felix", true)]
    [InlineData("fk", false)]
    [InlineData("felix.k", false)]
    [InlineData("a_very_long_handle_xyz", false)]
    public void Handle_validity(string handle, bool valid) => Assert.Equal(valid, StoreRules.IsValidHandle(handle));

    [Theory]
    [InlineData("FirebaseUid", "firebase_uid")]
    [InlineData("ParametersJson", "parameters_json")]
    [InlineData("Id", "id")]
    public void Columns_are_snake_case(string property, string column) => Assert.Equal(column, AppDbContext.ToSnakeCase(property));
}
