using SystevoTune.Engine.Platform;
using SystevoTune.Engine.Safety;
using SystevoTune.Engine.Tests.Fakes;

namespace SystevoTune.Engine.Tests.Safety;

public class RestorePointServiceTests
{
    private readonly FakeRegistryService _registry = new();
    private readonly FakeProcessRunner _processes = new();

    private RestorePointService NewService() => new(_registry, _processes);

    /// <summary>Restore is on unless the registry says otherwise, matching Windows' own default.</summary>
    private void EnableRestore() => _registry.With(RestorePointService.SessionInterval, RegistryValue.Dword(1));

    // ---- is it enabled ----

    [Fact]
    public void Restore_counts_as_enabled_when_the_registry_says_nothing()
        => Assert.True(NewService().IsSystemRestoreEnabled());

    [Fact]
    public void Restore_is_disabled_when_the_session_interval_is_zero()
    {
        _registry.With(RestorePointService.SessionInterval, RegistryValue.Dword(0));

        Assert.False(NewService().IsSystemRestoreEnabled());
    }

    [Fact]
    public void Restore_is_disabled_when_group_policy_switches_it_off()
    {
        EnableRestore();
        _registry.With(RestorePointService.DisabledByPolicy, RegistryValue.Dword(1));

        Assert.False(NewService().IsSystemRestoreEnabled());
    }

    [Fact]
    public void A_policy_value_of_zero_leaves_restore_enabled()
    {
        EnableRestore();
        _registry.With(RestorePointService.DisabledByPolicy, RegistryValue.Dword(0));

        Assert.True(NewService().IsSystemRestoreEnabled());
    }

    // ---- disabled: warn, never throw ----

    [Fact]
    public async Task Creating_a_point_while_restore_is_disabled_warns_instead_of_throwing()
    {
        _registry.With(RestorePointService.SessionInterval, RegistryValue.Dword(0));

        var result = await NewService().CreateAsync("Systevo Tune apply", CancellationToken.None);

        Assert.Equal(RestorePointStatus.Disabled, result.Status);
        Assert.True(result.NeedsUserDecision);
        Assert.False(result.Created);
    }

    [Fact]
    public async Task Nothing_is_run_when_restore_is_disabled()
    {
        _registry.With(RestorePointService.SessionInterval, RegistryValue.Dword(0));

        await NewService().CreateAsync("Systevo Tune apply", CancellationToken.None);

        Assert.Empty(_processes.Invocations);
    }

    [Fact]
    public async Task The_disabled_message_tells_the_user_what_to_do()
    {
        _registry.With(RestorePointService.SessionInterval, RegistryValue.Dword(0));

        var result = await NewService().CreateAsync("Systevo Tune apply", CancellationToken.None);

        Assert.Contains("System Protection", result.Message, StringComparison.Ordinal);
        Assert.Contains("Undo", result.Message, StringComparison.Ordinal);
    }

    // ---- the happy path: counts decide, not prose (O3, O5) ----

    /// <summary>The line the script emits. Our format, not Windows'.</summary>
    private static string Counts(int before, int after)
        => $"SYSTEVO_RP;before={before};after={after}";

    [Fact]
    public async Task A_point_appearing_is_what_counts_as_created()
    {
        EnableRestore();
        _processes.Returning(0, Counts(3, 4));

        var result = await NewService().CreateAsync("Systevo Tune apply", CancellationToken.None);

        Assert.Equal(RestorePointStatus.Created, result.Status);
        Assert.True(result.Created);
        Assert.False(result.NeedsUserDecision);
    }

    [Fact]
    public async Task The_first_ever_restore_point_counts_as_created()
    {
        EnableRestore();
        _processes.Returning(0, Counts(0, 1));

        Assert.Equal(
            RestorePointStatus.Created,
            (await NewService().CreateAsync("Systevo Tune apply", CancellationToken.None)).Status);
    }

    [Fact]
    public async Task The_description_and_both_counts_are_in_the_script()
    {
        EnableRestore();
        _processes.Returning(0, Counts(1, 2));

        await NewService().CreateAsync("Systevo Tune apply", CancellationToken.None);

        var script = _processes.LastArguments[^1];
        Assert.Contains("Get-ComputerRestorePoint", script, StringComparison.Ordinal);
        Assert.Contains("Checkpoint-Computer", script, StringComparison.Ordinal);
        Assert.Contains("Systevo Tune apply", script, StringComparison.Ordinal);
        Assert.Contains("MODIFY_SETTINGS", script, StringComparison.Ordinal);
    }

    [Fact]
    public void A_quote_in_the_description_cannot_break_out_of_the_command()
    {
        var command = RestorePointService.BuildArguments("it's a 'test'")[^1];

        Assert.Contains("-Description 'it''s a ''test'''", command, StringComparison.Ordinal);
    }

    [Fact]
    public void The_script_runs_under_windows_powershell_not_pwsh()
    {
        // Checkpoint-Computer does not exist in PowerShell 7 (decision 29).
        Assert.Contains("-NoProfile", RestorePointService.BuildArguments("x"));
        Assert.Contains("-NonInteractive", RestorePointService.BuildArguments("x"));
    }

    // ---- Windows declines ----

    [Fact]
    public async Task No_new_point_but_points_already_there_is_skipped_not_failed()
    {
        EnableRestore();
        _processes.Returning(0, Counts(5, 5));

        var result = await NewService().CreateAsync("Systevo Tune apply", CancellationToken.None);

        Assert.Equal(RestorePointStatus.Skipped, result.Status);
        Assert.True(result.NeedsUserDecision);
        Assert.Contains("5 restore point(s) already exist", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_arabic_windows_reaches_the_same_verdict_because_only_counts_are_read()
    {
        // Doc 07.4 requires non-English Windows to work. The prose here is nonsense to us; the
        // counts are not.
        EnableRestore();
        _processes.Returning(0,
            "تعذر إنشاء نقطة استعادة جديدة" + Environment.NewLine + Counts(2, 2));

        Assert.Equal(
            RestorePointStatus.Skipped,
            (await NewService().CreateAsync("Systevo Tune apply", CancellationToken.None)).Status);
    }

    [Fact]
    public async Task No_new_point_and_none_from_before_is_a_failure_with_nothing_to_fall_back_on()
    {
        EnableRestore();
        _processes.Returning(0, Counts(0, 0));

        var result = await NewService().CreateAsync("Systevo Tune apply", CancellationToken.None);

        Assert.Equal(RestorePointStatus.Failed, result.Status);
        Assert.Contains("none from earlier", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Windows_reporting_restore_as_off_is_reported_as_disabled()
    {
        // No counts at all means Get-ComputerRestorePoint itself failed.
        EnableRestore();
        _processes.Returning(1, standardError: "Checkpoint-Computer : System Restore is disabled.");

        var result = await NewService().CreateAsync("Systevo Tune apply", CancellationToken.None);

        Assert.Equal(RestorePointStatus.Disabled, result.Status);
    }

    // ---- failure ----

    [Fact]
    public async Task Output_with_no_counts_and_no_known_phrase_is_reported_as_failed()
    {
        EnableRestore();
        _processes.Returning(1, standardError: "Access is denied.");

        var result = await NewService().CreateAsync("Systevo Tune apply", CancellationToken.None);

        Assert.Equal(RestorePointStatus.Failed, result.Status);
        Assert.Contains("Access is denied.", result.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_half_written_count_line_is_ignored_rather_than_half_believed()
    {
        Assert.Null(RestorePointService.ParseCounts("SYSTEVO_RP;before=3"));
        Assert.Null(RestorePointService.ParseCounts("SYSTEVO_RP;before=x;after=y"));
        Assert.Equal((3, 4), RestorePointService.ParseCounts("SYSTEVO_RP;before=3;after=4"));
    }

    [Fact]
    public async Task A_process_that_will_not_start_is_reported_not_thrown()
    {
        EnableRestore();
        _processes.Throwing(new InvalidOperationException("powershell.exe not found"));

        var result = await NewService().CreateAsync("Systevo Tune apply", CancellationToken.None);

        Assert.Equal(RestorePointStatus.Failed, result.Status);
        Assert.Contains("powershell.exe not found", result.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_is_the_one_thing_that_still_throws()
    {
        EnableRestore();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => NewService().CreateAsync("Systevo Tune apply", cancellation.Token));
    }

    [Fact]
    public async Task An_empty_description_is_a_caller_bug()
        => await Assert.ThrowsAsync<ArgumentException>(
            () => NewService().CreateAsync("   ", CancellationToken.None));
}
