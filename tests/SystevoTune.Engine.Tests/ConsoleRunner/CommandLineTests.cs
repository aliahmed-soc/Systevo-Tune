using SystevoTune.ConsoleRunner;

namespace SystevoTune.Engine.Tests.ConsoleRunner;

/// <summary>
/// The interlock that stands between a mistyped command and a real desktop.
/// Tested rather than run: the ConsoleRunner is never executed on a dev machine.
/// </summary>
public class CommandLineTests
{
    // ---- parsing ----

    [Fact]
    public void No_arguments_means_help()
        => Assert.Equal("help", CommandLine.Parse([]).Command);

    [Fact]
    public void The_first_word_is_the_command_and_the_second_is_its_operand()
    {
        var line = CommandLine.Parse(["preview", "gaming"]);

        Assert.Equal("preview", line.Command);
        Assert.Equal("gaming", line.Argument);
    }

    [Fact]
    public void The_vm_flag_is_not_mistaken_for_a_profile_name()
    {
        var line = CommandLine.Parse(["apply", "--vm", "gaming"]);

        Assert.Equal("gaming", line.Argument);
        Assert.True(line.VmConfirmed);
    }

    [Fact]
    public void The_vm_flag_is_recognised_after_the_profile_too()
        => Assert.True(CommandLine.Parse(["apply", "gaming", "--vm"]).VmConfirmed);

    [Fact]
    public void Without_the_flag_nothing_is_confirmed()
        => Assert.False(CommandLine.Parse(["apply", "gaming"]).VmConfirmed);

    // ---- which commands write ----

    [Theory]
    [InlineData("apply")]
    [InlineData("reapply")]
    [InlineData("undo")]
    public void Apply_reapply_and_undo_are_the_commands_that_change_the_machine(string command)
        => Assert.True(CommandLine.Parse([command]).ChangesTheMachine);

    [Fact]
    public void Reapply_is_behind_the_same_interlock_as_apply()
        => Assert.Equal(GuardResult.NeedsVmFlag, CommandLine.Parse(["reapply"]).Check(isElevated: true));

    [Theory]
    [InlineData("scan")]
    [InlineData("preview")]
    [InlineData("profiles")]
    [InlineData("startup")]
    [InlineData("runs")]
    [InlineData("help")]
    public void Every_other_command_only_reads(string command)
        => Assert.False(CommandLine.Parse([command]).ChangesTheMachine);

    // ---- the guard ----

    [Theory]
    [InlineData("scan")]
    [InlineData("preview")]
    [InlineData("runs")]
    public void A_read_only_command_runs_without_the_flag_or_admin_rights(string command)
        => Assert.Equal(GuardResult.Allowed, CommandLine.Parse([command]).Check(isElevated: false));

    [Theory]
    [InlineData("apply")]
    [InlineData("undo")]
    public void A_writing_command_without_the_flag_is_refused_even_as_admin(string command)
        => Assert.Equal(GuardResult.NeedsVmFlag, CommandLine.Parse([command, "gaming"]).Check(isElevated: true));

    [Fact]
    public void A_writing_command_with_the_flag_but_no_admin_rights_is_refused()
        => Assert.Equal(
            GuardResult.NeedsElevation,
            CommandLine.Parse(["apply", "gaming", "--vm"]).Check(isElevated: false));

    [Fact]
    public void A_writing_command_with_both_may_run()
        => Assert.Equal(
            GuardResult.Allowed,
            CommandLine.Parse(["apply", "gaming", "--vm"]).Check(isElevated: true));

    [Fact]
    public void The_missing_flag_is_reported_before_the_missing_admin_rights()
    {
        // A user who mistypes apply on their own desktop should be told they are not in a VM,
        // not sent off to find an admin prompt and try again.
        var line = CommandLine.Parse(["apply", "gaming"]);

        Assert.Equal(GuardResult.NeedsVmFlag, line.Check(isElevated: false));
    }
}
