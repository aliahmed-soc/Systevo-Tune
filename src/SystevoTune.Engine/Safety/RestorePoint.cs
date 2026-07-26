namespace SystevoTune.Engine.Safety;

/// <summary>How an attempt to create a System Restore point ended.</summary>
public enum RestorePointStatus
{
    /// <summary>A restore point was created.</summary>
    Created,

    /// <summary>System Restore is switched off on this PC. Nothing was created.</summary>
    Disabled,

    /// <summary>Windows declined because it made one recently. An earlier point still exists.</summary>
    Skipped,

    /// <summary>The attempt failed for another reason.</summary>
    Failed,
}

/// <summary>
/// Result of asking Windows for a restore point. Doc 5.1 requires a warning rather than an
/// exception when restore is off, so the caller can show it and let the user decide.
/// </summary>
/// <param name="Status">What happened.</param>
/// <param name="Message">Plain text for the user. Always populated.</param>
/// <param name="Detail">Raw output when something failed, for the log. <c>null</c> otherwise.</param>
public sealed record RestorePointResult(RestorePointStatus Status, string Message, string? Detail = null)
{
    /// <summary>A restore point now exists because of this call.</summary>
    public bool Created => Status is RestorePointStatus.Created;

    /// <summary>
    /// The user must be asked before applying changes. True for everything except a fresh
    /// restore point — including <see cref="RestorePointStatus.Skipped"/>, because "Windows made
    /// one recently" is a claim the user should get to weigh rather than have assumed for them.
    /// </summary>
    public bool NeedsUserDecision => Status is not RestorePointStatus.Created;
}

/// <summary>
/// Creates a System Restore point before an apply run. Behind an interface so tests never
/// trigger a real one.
/// </summary>
public interface IRestorePointService
{
    /// <summary>Whether System Restore is switched on for the system drive.</summary>
    bool IsSystemRestoreEnabled();

    /// <summary>
    /// Asks Windows for a restore point. Never throws for an expected condition — restore being
    /// off, or Windows declining, both come back as a result the caller shows to the user.
    /// </summary>
    Task<RestorePointResult> CreateAsync(string description, CancellationToken cancellationToken);
}
