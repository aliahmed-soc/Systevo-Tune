namespace SystevoTune.ConsoleRunner;

/// <summary>Why a command was refused, or that it may go ahead.</summary>
internal enum GuardResult
{
    /// <summary>Nothing stands in the way.</summary>
    Allowed,

    /// <summary>The command changes the machine and <c>--vm</c> was not given.</summary>
    NeedsVmFlag,

    /// <summary>The command needs administrator rights this process does not have.</summary>
    NeedsElevation,
}

/// <summary>
/// The parsed command line. Pure — no engine, no machine — so the guard that stands between a
/// mistyped command and a real desktop is unit tested.
/// </summary>
internal sealed record CommandLine(string Command, string? Argument, bool VmConfirmed)
{
    /// <summary>Guard flag for anything that changes the machine.</summary>
    public const string VmFlag = "--vm";

    /// <summary>Commands that write to the system. Everything else only reads.</summary>
    private static readonly string[] Writing = ["apply", "reapply", "undo", "verify"];

    /// <summary>Whether this command would change system state.</summary>
    public bool ChangesTheMachine => Writing.Contains(Command, StringComparer.Ordinal);

    /// <summary>Splits arguments into a command, its first operand, and the VM flag.</summary>
    public static CommandLine Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var confirmed = args.Contains(VmFlag, StringComparer.Ordinal);
        var positional = args.Where(argument => !argument.StartsWith("--", StringComparison.Ordinal)).ToArray();

        return new CommandLine(
            positional.Length > 0 ? positional[0] : "help",
            positional.Length > 1 ? positional[1] : null,
            confirmed);
    }

    /// <summary>
    /// Whether the command may run. A read-only command always may; a writing command needs both
    /// the VM flag and administrator rights.
    /// </summary>
    /// <remarks>
    /// The VM flag is checked first on purpose: a user who mistypes <c>apply</c> on their own
    /// desktop should be told they are not in a VM, not sent off to find an admin prompt.
    /// </remarks>
    public GuardResult Check(bool isElevated)
    {
        if (!ChangesTheMachine)
        {
            return GuardResult.Allowed;
        }

        if (!VmConfirmed)
        {
            return GuardResult.NeedsVmFlag;
        }

        return isElevated ? GuardResult.Allowed : GuardResult.NeedsElevation;
    }
}
