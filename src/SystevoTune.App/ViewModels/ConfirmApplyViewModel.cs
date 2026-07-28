using SystevoTune.Engine.Safety;

namespace SystevoTune.App.ViewModels;

/// <summary>
/// The second step of Apply. Doc 5.5: preview first, then a separate deliberate confirmation.
/// </summary>
/// <remarks>
/// A6: this dialog is where the restore point is actually attempted, before any tweak runs. If
/// <see cref="IRestorePointService"/> comes back with anything other than Created, the warning is
/// shown in red and the user has to read past it. It never blocks — doc 5.1 says warn and ask,
/// not refuse — but it never hides it either.
/// </remarks>
public sealed class ConfirmApplyViewModel : ObservableObject
{
    private readonly IRestorePointService _restorePoints;
    private readonly bool _restorePointsWanted;

    private bool _isChecking;
    private RestorePointResult? _result;
    private bool _confirmed;

    /// <param name="restorePoints">The restore point service.</param>
    /// <param name="changeCount">How many changes are about to be made.</param>
    /// <param name="restorePointDescription">The name the restore point will carry.</param>
    /// <param name="restorePointsWanted">
    /// The Settings toggle. When off, no restore point is attempted and the dialog says so —
    /// B4 requires that choice to be visible here, not silently honoured.
    /// </param>
    public ConfirmApplyViewModel(
        IRestorePointService restorePoints,
        int changeCount,
        string restorePointDescription,
        bool restorePointsWanted = true)
    {
        _restorePoints = restorePoints;
        _restorePointsWanted = restorePointsWanted;

        ChangeCount = changeCount;
        RestorePointDescription = restorePointDescription;
    }

    /// <summary>How many changes are about to be made.</summary>
    public int ChangeCount { get; }

    /// <summary>The name the restore point will carry, shown to the user.</summary>
    public string RestorePointDescription { get; }

    /// <summary>Whether the restore point attempt is in flight.</summary>
    public bool IsChecking
    {
        get => _isChecking;
        private set => Set(ref _isChecking, value);
    }

    /// <summary>What the restore point attempt produced. <c>null</c> until it has run.</summary>
    public RestorePointResult? Result
    {
        get => _result;
        private set
        {
            if (Set(ref _result, value))
            {
                Raise(nameof(HasWarning));
                Raise(nameof(WarningKey));
                Raise(nameof(RestorePointCreated));
            }
        }
    }

    /// <summary>
    /// Whether a warning must be shown in red before the user can continue.
    /// </summary>
    /// <remarks>
    /// True whenever a restore point was not created — including when the user switched them off.
    /// Turning the safety net off is exactly the moment to say so again.
    /// </remarks>
    public bool HasWarning => !_restorePointsWanted || (Result is not null && Result.NeedsUserDecision);

    /// <summary>A restore point exists because of this run.</summary>
    public bool RestorePointCreated => Result?.Created ?? false;

    /// <summary>
    /// The localisation key for the warning, so the message is translated rather than passed
    /// through from the engine in English.
    /// </summary>
    public string? WarningKey
    {
        get
        {
            if (!_restorePointsWanted)
            {
                return "Confirm_RestoreOff";
            }

            return Result?.Status switch
            {
                RestorePointStatus.Disabled => "Confirm_RestoreDisabled",
                RestorePointStatus.Skipped => "Confirm_RestoreSkipped",
                RestorePointStatus.Failed => "Confirm_RestoreFailed",
                _ => null,
            };
        }
    }

    /// <summary>The engine's own wording, shown under the translated headline as detail.</summary>
    public string? EngineMessage => Result?.Message;

    /// <summary>Whether the user pressed the confirm button.</summary>
    public bool Confirmed
    {
        get => _confirmed;
        private set => Set(ref _confirmed, value);
    }

    /// <summary>
    /// Attempts the restore point. Called when the dialog opens, before the user can confirm, so
    /// they are deciding with the answer in front of them.
    /// </summary>
    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        if (!_restorePointsWanted)
        {
            // Nothing attempted. HasWarning already covers this case.
            return;
        }

        IsChecking = true;

        try
        {
            Result = await _restorePoints.CreateAsync(RestorePointDescription, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // The service is documented not to throw for expected conditions, so anything landing
            // here is unexpected — and still must not take the window down mid-decision.
            Result = new RestorePointResult(RestorePointStatus.Failed, ex.Message);
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>The user chose to go ahead.</summary>
    public void Confirm() => Confirmed = true;
}
