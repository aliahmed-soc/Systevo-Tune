using System.Globalization;
using SystevoTune.Engine;
using SystevoTune.Engine.Cleanup;
using SystevoTune.Engine.Profiles;
using SystevoTune.Engine.Safety;
using SystevoTune.Engine.Startup;
using SystevoTune.Engine.Tweaks;

namespace SystevoTune.ConsoleRunner;

/// <summary>The console commands. Formatting only — every decision belongs to the engine.</summary>
internal static class Commands
{
    public static async Task<int> RunAsync(string[] args)
    {
        var line = CommandLine.Parse(args);

        if (line.Command is "help" or "-h" or "--help")
        {
            PrintUsage();
            return 0;
        }

        try
        {
            var host = EngineHost.Create();

            // Checked before the command runs, so a refused command has done nothing at all.
            switch (line.Check(host.Elevation.IsElevated))
            {
                case GuardResult.NeedsVmFlag:
                    return RefuseUnconfirmed();
                case GuardResult.NeedsElevation:
                    return RefuseUnelevated();
                default:
                    break;
            }

            return line.Command switch
            {
                "scan" => Scan(host),
                "profiles" => ListProfiles(host),
                "startup" => ListStartup(host),
                "runs" => ListRuns(host),
                "preview" => await PreviewAsync(host, line).ConfigureAwait(false),
                "apply" => await ApplyAsync(host, line).ConfigureAwait(false),
                "reapply" => await ReapplyAsync(host).ConfigureAwait(false),
                "verify" => TryResolveProfile(host, line, out var target)
                    ? await VerifyCommand.RunAsync(host, target).ConfigureAwait(false)
                    : 2,
                "undo" => await UndoAsync(host).ConfigureAwait(false),
                _ => Unknown(line.Command),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed: {ex.Message}");
            return 1;
        }
    }

    // ---- read-only commands ----

    private static int Scan(EngineHost host)
    {
        var report = host.Cleanup.Scan();

        Console.WriteLine("Cleanup scan");
        foreach (var group in report.Groups)
        {
            Console.WriteLine(
                $"  {group.NameEn,-24} {group.HumanSize,10}  ({group.FileCount} files)");

            foreach (var rejected in group.RejectedPaths)
            {
                Console.WriteLine($"      REJECTED BY GUARD: {rejected}");
            }
        }

        Console.WriteLine($"  {"Total",-24} {report.HumanTotal,10}  ({report.TotalFiles} files)");
        return 0;
    }

    private static int ListProfiles(EngineHost host)
    {
        foreach (var profile in host.Profiles.Profiles)
        {
            Console.WriteLine($"{profile.Id,-10} {profile.NameEn} — {profile.DescriptionEn}");
        }

        return 0;
    }

    private static int ListStartup(EngineHost host)
    {
        var items = host.Startup.List();
        if (items.Count == 0)
        {
            Console.WriteLine("No startup items found.");
            return 0;
        }

        foreach (var item in items)
        {
            Console.WriteLine($"  [{(item.State is StartupState.Enabled ? "on " : "off")}] {item.Name,-30} {item.Command}");
        }

        return 0;
    }

    private static int ListRuns(EngineHost host)
    {
        var runs = host.Log.ReadAllRuns();
        if (runs.Count == 0)
        {
            Console.WriteLine($"No runs logged in {host.Log.DirectoryPath}");
            return 0;
        }

        foreach (var run in runs)
        {
            var pending = run.Records.Count(record => !record.Undone && record.Undoable);
            Console.WriteLine($"  {run.RunId}  {run.Records.Count} records, {pending} still to undo");

            if (run.SkippedLineCount > 0)
            {
                Console.WriteLine($"      {run.SkippedLineCount} unreadable line(s) — a run may have been killed mid-change");
            }
        }

        return 0;
    }

    private static async Task<int> PreviewAsync(EngineHost host, CommandLine line)
    {
        if (!TryResolveProfile(host, line, out var profile))
        {
            return 2;
        }

        var preview = await host.Runner.PreviewAsync(host.ProfileBuilder.Build(profile)).ConfigureAwait(false);

        Console.WriteLine($"Preview of '{profile.NameEn}' — nothing has been changed.");
        foreach (var plan in preview.Plans)
        {
            Console.WriteLine($"  {plan.TweakName} [{plan.Status}]");

            if (plan.Message is not null)
            {
                Console.WriteLine($"      {plan.Message}");
            }

            foreach (var change in plan.Changes)
            {
                Console.WriteLine($"      {change.Description}");
                Console.WriteLine($"        {change.Target}");
                Console.WriteLine($"        {change.OldValue ?? "(not set)"} -> {change.NewValue ?? "(removed)"}"
                    + (change.Undoable ? string.Empty : "   PERMANENT, cannot be undone"));
            }
        }

        Console.WriteLine($"  {preview.AllChanges.Count} change(s) in total.");

        if (preview.RequiresRestart)
        {
            Console.WriteLine("  A restart is needed before some of these take full effect.");
        }

        return 0;
    }

    // ---- commands that change the machine ----

    private static async Task<int> ApplyAsync(EngineHost host, CommandLine line)
    {
        if (!TryResolveProfile(host, line, out var profile))
        {
            return 2;
        }

        var restore = await host.RestorePoints
            .CreateAsync($"{EngineInfo.ProductName}: before {profile.NameEn}", CancellationToken.None)
            .ConfigureAwait(false);

        Console.WriteLine($"Restore point: {restore.Status} — {restore.Message}");

        if (restore.NeedsUserDecision)
        {
            Console.WriteLine("  Doc 5.1 says the user is asked here. This harness continues so the");
            Console.WriteLine("  VM test can run; the WPF app must stop and ask instead.");
        }

        var run = host.Log.StartRun();
        Console.WriteLine($"Run {run.RunId} — log at {run.FilePath}");

        // ProfileApplier notes which profile this was, so `reapply` can find it later.
        var applied = await host.ProfileApplier.ApplyAsync(profile, run).ConfigureAwait(false);
        var report = applied.Report;
        var tweaks = applied.Tweaks;

        foreach (var outcome in report.Outcomes)
        {
            Console.WriteLine($"  {outcome.TweakName} [{outcome.Status}] {outcome.Applied.Count} applied");

            foreach (var failure in outcome.Failures)
            {
                Console.WriteLine($"      FAILED {failure.Change.Target}: {failure.Reason}");
            }
        }

        foreach (var cleanup in tweaks.OfType<CleanupTweak>())
        {
            if (cleanup.LastApply is not { } detail)
            {
                continue;
            }

            Console.WriteLine(detail.WasSkipped
                ? $"  {cleanup.Name}: SKIPPED — {detail.SkippedReason}"
                : $"  {cleanup.Name}: freed {detail.HumanFreed}, {detail.FilesLocked} file(s) in use and left alone");
        }

        Console.WriteLine($"  {report.AllApplied.Count} applied, {report.AllFailures.Count} failed.");

        if (report.RequiresRestart)
        {
            Console.WriteLine("  Restart needed before some changes take full effect.");
        }

        return report.AllSucceeded ? 0 : 1;
    }

    /// <summary>Doc 5.6: Windows updates reset tweaks, so run the last profile again.</summary>
    private static async Task<int> ReapplyAsync(EngineHost host)
    {
        var target = host.Reapply.FindLast();
        if (target is null)
        {
            Console.Error.WriteLine("No profile has been applied yet, so there is nothing to re-apply.");
            return 2;
        }

        Console.WriteLine(
            $"Last applied '{target.ProfileId}' in run {target.RunId} "
            + $"({target.AppliedAt:yyyy-MM-dd HH:mm}, {target.ChangeCount} change(s)).");

        return await ApplyAsync(host, new CommandLine("apply", target.ProfileId, VmConfirmed: true))
            .ConfigureAwait(false);
    }

    private static async Task<int> UndoAsync(EngineHost host)
    {
        var report = await host.NewUndoEngine().UndoAllAsync().ConfigureAwait(false);

        Console.WriteLine($"Undo All — {report.Undone.Count} restored, {report.Failures.Count} failed.");

        foreach (var record in report.Undone)
        {
            Console.WriteLine($"  restored {record.Target} to {record.OldValue ?? "(not set)"}");
        }

        foreach (var failure in report.Failures)
        {
            Console.WriteLine($"  FAILED {failure.Record?.Target ?? failure.RecordId}: {failure.Reason}");
        }

        if (report.Permanent.Count > 0)
        {
            Console.WriteLine($"  {report.Permanent.Count} change(s) cannot be undone — deleted files do not come back:");
            foreach (var record in report.Permanent)
            {
                Console.WriteLine($"      {record.Target} ({record.OldValue})");
            }
        }

        return report.AllSucceeded ? 0 : 1;
    }

    // ---- guards ----

    /// <summary>The project rules: apply and undo only ever run inside a VM.</summary>
    private static int RefuseUnconfirmed()
    {
        Console.Error.WriteLine("Refusing to change this machine.");
        Console.Error.WriteLine($"This command changes system state. Re-run with {CommandLine.VmFlag} to confirm you are");
        Console.Error.WriteLine("inside a throwaway virtual machine with a snapshot taken.");
        return 2;
    }

    /// <summary>Doc 07.4: no admin rights means a clean message and zero half-changes.</summary>
    private static int RefuseUnelevated()
    {
        Console.Error.WriteLine("Administrator rights are needed and this process does not have them.");
        Console.Error.WriteLine("Nothing was changed. Re-run from an elevated prompt.");
        return 2;
    }

    private static bool TryResolveProfile(EngineHost host, CommandLine line, out Profile profile)
    {
        var id = line.Argument;
        if (id is null)
        {
            Console.Error.WriteLine("Name a profile. Try: profiles");
            profile = null!;
            return false;
        }

        var found = host.Profiles.Find(id);
        if (found is null)
        {
            Console.Error.WriteLine($"'{id}' is not a profile. Try: profiles");
            profile = null!;
            return false;
        }

        profile = found;
        return true;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{EngineInfo.ProductName} ConsoleRunner — engine v{EngineInfo.Version} (dev harness, not for users)"));
        Console.WriteLine();
        Console.WriteLine("  scan                  size of each cleanup group. Changes nothing.");
        Console.WriteLine("  profiles              list the presets.");
        Console.WriteLine("  startup               list startup items and whether they run.");
        Console.WriteLine("  runs                  list logged runs and what is still to undo.");
        Console.WriteLine("  preview <profile>     full dry run: old -> new for every change.");
        Console.WriteLine("  apply <profile> --vm  restore point, then apply. CHANGES THE MACHINE.");
        Console.WriteLine("  reapply --vm          run the last applied profile again. CHANGES THE MACHINE.");
        Console.WriteLine("  undo --vm             Undo All, newest first. CHANGES THE MACHINE.");
        Console.WriteLine("  verify <profile> --vm doc 07.2 in one go: snapshot, apply, undo, compare.");
        Console.WriteLine("                        CHANGES THE MACHINE and puts it back. Exit 0 = pass.");
        Console.WriteLine();
        Console.WriteLine($"  apply and undo refuse to run without {CommandLine.VmFlag}. Only ever use them in a VM");
        Console.WriteLine("  that has a snapshot you can roll back to.");
    }
}
