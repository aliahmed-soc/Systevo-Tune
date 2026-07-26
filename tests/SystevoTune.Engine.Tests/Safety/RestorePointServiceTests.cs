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

    // ---- the happy path ----

    [Fact]
    public async Task A_clean_run_reports_the_point_as_created()
    {
        EnableRestore();
        _processes.Returning(0);

        var result = await NewService().CreateAsync("Systevo Tune apply", CancellationToken.None);

        Assert.Equal(RestorePointStatus.Created, result.Status);
        Assert.True(result.Created);
        Assert.False(result.NeedsUserDecision);
    }

    [Fact]
    public async Task The_description_is_passed_through_to_the_checkpoint_command()
    {
        EnableRestore();
        _processes.Returning(0);

        await NewService().CreateAsync("Systevo Tune apply", CancellationToken.None);

        Assert.Contains("Checkpoint-Computer", _processes.LastArguments[^1], StringComparison.Ordinal);
        Assert.Contains("Systevo Tune apply", _processes.LastArguments[^1], StringComparison.Ordinal);
        Assert.Contains("MODIFY_SETTINGS", _processes.LastArguments[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_quote_in_the_description_cannot_break_out_of_the_command()
    {
        var command = RestorePointService.BuildArguments("it's a 'test'")[^1];

        Assert.Contains("-Description 'it''s a ''test''' ", command, StringComparison.Ordinal);
    }

    // ---- Windows declines ----

    [Fact]
    public async Task A_recent_restore_point_is_reported_as_skipped_not_failed()
    {
        EnableRestore();
        _processes.Returning(0, standardError:
            "WARNING: A new system restore point cannot be created because one has already been created within the past 1440 minutes.");

        var result = await NewService().CreateAsync("Systevo Tune apply", CancellationToken.None);

        Assert.Equal(RestorePointStatus.Skipped, result.Status);
        Assert.True(result.NeedsUserDecision);
    }

    [Fact]
    public async Task Windows_reporting_restore_as_off_is_reported_as_disabled()
    {
        EnableRestore();
        _processes.Returning(1, standardError: "Checkpoint-Computer : System Restore is disabled.");

        var result = await NewService().CreateAsync("Systevo Tune apply", CancellationToken.None);

        Assert.Equal(RestorePointStatus.Disabled, result.Status);
    }

    // ---- failure ----

    [Fact]
    public async Task A_non_zero_exit_code_is_reported_as_failed_with_the_output_kept()
    {
        EnableRestore();
        _processes.Returning(1, standardError: "Access is denied.");

        var result = await NewService().CreateAsync("Systevo Tune apply", CancellationToken.None);

        Assert.Equal(RestorePointStatus.Failed, result.Status);
        Assert.Contains("Access is denied.", result.Detail!, StringComparison.Ordinal);
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
