using Xunit;

namespace WinOrbit.Tests;

public class JitProfileTests
{
    [Theory]
    [InlineData()]
    [InlineData("--startup")]
    [InlineData("--page", "widgets")]
    [InlineData("--demo", "call")]
    public void The_copy_that_keeps_running_records(params string[] args) =>
        Assert.True(Program.RecordsJitProfile(args, otherCopyRunning: false));

    [Theory]
    [InlineData()]
    [InlineData("--startup")]
    [InlineData("--page", "widgets")]
    public void A_second_copy_handing_over_to_the_running_one_does_not(params string[] args) =>
        Assert.False(Program.RecordsJitProfile(args, otherCopyRunning: true));

    [Theory]
    [InlineData("--quit")]
    [InlineData("--QUIT")]
    [InlineData("--revert-tweaks")]
    [InlineData("--wmp-link", "1234")]
    public void Short_lived_commands_do_not_even_as_the_only_copy(params string[] args) =>
        Assert.False(Program.RecordsJitProfile(args, otherCopyRunning: false));

    [Fact]
    public void Profile_is_not_json_so_backups_leave_it_out() =>
        Assert.False(Program.JitProfileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
}
