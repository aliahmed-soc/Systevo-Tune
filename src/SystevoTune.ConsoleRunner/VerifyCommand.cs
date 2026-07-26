using System.Globalization;
using System.Text;
using SystevoTune.Engine;
using SystevoTune.Engine.Profiles;
using SystevoTune.Engine.Verification;

namespace SystevoTune.ConsoleRunner;

/// <summary>
/// The `verify` command: doc 07.2's key test, run end to end inside the VM.
/// </summary>
/// <remarks>
/// Formatting and file writing only — the cycle itself lives in
/// <see cref="VerificationRunner"/> so it can be unit tested without a console.
/// </remarks>
internal static class VerifyCommand
{
    /// <summary>Where the snapshots and the report are written.</summary>
    internal static string OutputRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        EngineInfo.ProductName,
        "verify");

    public static async Task<int> RunAsync(EngineHost host, Profile profile)
    {
        Console.WriteLine($"Verifying '{profile.NameEn}' — doc 07.2 cycle.");
        Console.WriteLine("  This CHANGES the machine and puts it back. Only ever run it in a VM with a snapshot.");
        Console.WriteLine();

        var report = await host.Verification.RunAsync(profile).ConfigureAwait(false);
        var folder = WriteArtifacts(report);

        Console.WriteLine($"1. before      {SystemStateCollector.DescribeCoverage(report.Before)}");
        Console.WriteLine($"2. apply       {report.Apply.AllApplied.Count} change(s), {report.Apply.AllFailures.Count} failure(s)");
        Console.WriteLine($"3. after-apply {report.AppliedChanges.Count} difference(s) from the start — this is what the profile did");
        Console.WriteLine($"4. undo        {report.Undo.Undone.Count} restored, {report.Undo.Failures.Count} failed, "
            + $"{report.Undo.Permanent.Count} permanent");
        Console.WriteLine($"5. after-undo  compared against step 1");
        Console.WriteLine();

        foreach (var failure in report.Apply.AllFailures)
        {
            Console.WriteLine($"  apply failed: {failure.Change.Target} — {failure.Reason}");
        }

        foreach (var failure in report.Undo.Failures)
        {
            Console.WriteLine($"  undo failed:  {failure.Record?.Target ?? failure.RecordId} — {failure.Reason}");
        }

        Console.WriteLine();

        if (!report.ProvedAnything)
        {
            // A machine already in the target state proves nothing, and a green line here would
            // read as a pass. Say so rather than let it look like one.
            Console.WriteLine("INCONCLUSIVE — the profile changed nothing, so there was nothing to undo.");
            Console.WriteLine("  Roll the VM back to a clean snapshot and run this before applying anything else.");
            Console.WriteLine($"  Artifacts: {folder}");
            return 2;
        }

        if (report.ReturnedToStart)
        {
            Console.WriteLine("PASS — the PC is exactly as it was before the run.");
            Console.WriteLine($"  Artifacts: {folder}");
            return 0;
        }

        Console.WriteLine($"FAIL — {report.Differences.Count} difference(s) remain. Doc 07.2: any difference is a bug.");
        Console.WriteLine();

        foreach (var difference in report.Differences)
        {
            Console.WriteLine("  " + difference.ToString().Replace("\n", "\n  ", StringComparison.Ordinal));
        }

        Console.WriteLine();
        Console.WriteLine($"  Artifacts: {folder}");
        return 1;
    }

    /// <summary>Writes the three snapshots and a Markdown report next to them.</summary>
    private static string WriteArtifacts(VerificationReport report)
    {
        var folder = Path.Combine(
            OutputRoot,
            string.Create(CultureInfo.InvariantCulture, $"{report.RunId}-{report.ProfileId}"));

        Directory.CreateDirectory(folder);

        File.WriteAllText(Path.Combine(folder, "1-before.json"), report.Before.ToJson());
        File.WriteAllText(Path.Combine(folder, "2-after-apply.json"), report.AfterApply.ToJson());
        File.WriteAllText(Path.Combine(folder, "3-after-undo.json"), report.AfterUndo.ToJson());
        File.WriteAllText(Path.Combine(folder, "report.md"), BuildMarkdown(report));

        return folder;
    }

    internal static string BuildMarkdown(VerificationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"# Verification report — {report.ProfileId}");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Run `{report.RunId}`, taken {report.Before.TakenAt:yyyy-MM-dd HH:mm}.");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture,
            $"**Result: {Verdict(report)}**");
        text.AppendLine();
        text.AppendLine("Doc 07.2: snapshot, apply everything, Undo All, compare. Any difference is a bug.");
        text.AppendLine();

        text.AppendLine("## Coverage");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"- {SystemStateCollector.DescribeCoverage(report.Before)}");
        text.AppendLine();

        text.AppendLine("## What the profile changed");
        text.AppendLine();
        AppendDifferences(text, report.AppliedChanges, "The profile changed nothing on this PC.");

        text.AppendLine("## What is still different after Undo All");
        text.AppendLine();
        AppendDifferences(text, report.Differences, "Nothing. The PC is exactly as it was.");

        if (report.Undo.Permanent.Count > 0)
        {
            text.AppendLine("## Permanent by design");
            text.AppendLine();
            text.AppendLine("These were never undoable — deleted files do not come back.");
            text.AppendLine();
            foreach (var record in report.Undo.Permanent)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- `{record.Target}` ({record.OldValue})");
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    private static void AppendDifferences(StringBuilder text, IReadOnlyList<StateDifference> differences, string ifNone)
    {
        if (differences.Count == 0)
        {
            text.AppendLine(ifNone);
            text.AppendLine();
            return;
        }

        text.AppendLine("| Area | Target | Was | Now |");
        text.AppendLine("|---|---|---|---|");

        foreach (var difference in differences)
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| {difference.Area} | `{difference.Target}` | {Cell(difference.Before)} | {Cell(difference.After)} |");
        }

        text.AppendLine();
    }

    private static string Cell(string? value) => value is null ? "_(not set)_" : "`" + value + "`";

    private static string Verdict(VerificationReport report) => report switch
    {
        { ProvedAnything: false } => "INCONCLUSIVE — the profile changed nothing, so nothing was proved",
        { ReturnedToStart: true } => "PASS — the PC returned to its starting state",
        _ => $"FAIL — {report.Differences.Count} difference(s) remain",
    };
}
