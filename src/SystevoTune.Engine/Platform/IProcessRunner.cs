namespace SystevoTune.Engine.Platform;

/// <summary>What a finished process left behind.</summary>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>Both streams together, for matching against known messages.</summary>
    public string AllOutput => StandardOutput + Environment.NewLine + StandardError;
}

/// <summary>
/// Runs an external program. Wrapped so the engine's logic stays testable: doc 02 has some
/// work going through PowerShell, and none of it may run during a unit test.
/// </summary>
public interface IProcessRunner
{
    /// <summary>Runs the program and waits for it. Arguments are passed as a list, never a joined string.</summary>
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}
