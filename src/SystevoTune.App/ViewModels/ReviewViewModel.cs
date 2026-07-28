using System.Collections.ObjectModel;
using SystevoTune.Engine.Profiles;
using SystevoTune.Engine.Tweaks;

namespace SystevoTune.App.ViewModels;

/// <summary>One tickable change on the review screen.</summary>
public sealed class ChangeRow : ObservableObject
{
    private bool _isSelected = true;

    internal ChangeRow(string tweakId, string tweakName, PlannedChange change, bool requiresRestart)
    {
        TweakId = tweakId;
        TweakName = tweakName;
        Change = change;
        RequiresRestart = requiresRestart;
    }

    /// <summary>Which tweak this change belongs to. Groups the list.</summary>
    public string TweakId { get; }

    /// <summary>Tweak name, used as the group heading.</summary>
    public string TweakName { get; }

    /// <summary>The change itself.</summary>
    public PlannedChange Change { get; }

    /// <summary>Whether this change needs a restart to take full effect.</summary>
    public bool RequiresRestart { get; }

    /// <summary>Whether the user wants this applied.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    /// <summary>Cleanup deletions cannot be put back, and the row says so.</summary>
    public bool IsPermanent => !Change.Undoable;

    /// <summary>What it does, in the engine's own words.</summary>
    public string Description => Change.Description;

    /// <summary>The old value for display.</summary>
    public string? OldValue => Change.OldValue;

    /// <summary>The new value for display.</summary>
    public string? NewValue => Change.NewValue;
}

/// <summary>A tweak and its changes, as one collapsible block.</summary>
public sealed class ChangeGroup(string tweakId, string tweakName, IReadOnlyList<ChangeRow> rows)
{
    /// <summary>Tweak id.</summary>
    public string TweakId { get; } = tweakId;

    /// <summary>Heading.</summary>
    public string TweakName { get; } = tweakName;

    /// <summary>The changes under it.</summary>
    public IReadOnlyList<ChangeRow> Rows { get; } = rows;
}

/// <summary>
/// Screen 2. Doc 5.5: the user sees the full list before anything happens, and applying is a
/// separate decision.
/// </summary>
/// <remarks>
/// Nothing here touches the system. It previews, lists, and hands the selection on — the confirm
/// dialog and then the apply screen are the two further steps before anything is written.
/// </remarks>
public sealed class ReviewViewModel : ObservableObject
{
    private readonly TweakRunner _runner;
    private readonly ProfileBuilder _builder;

    private Profile? _selectedProfile;
    private bool _isBusy;
    private string? _error;
    private bool _hasPreviewed;
    private bool _requiresRestart;

    /// <param name="runner">Preview runner.</param>
    /// <param name="builder">Turns a profile into tweaks.</param>
    /// <param name="profiles">Presets offered in the picker.</param>
    public ReviewViewModel(TweakRunner runner, ProfileBuilder builder, ProfileCatalog profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        _runner = runner;
        _builder = builder;

        Profiles = new ObservableCollection<Profile>(profiles.Profiles);
        _selectedProfile = Profiles.FirstOrDefault();

        SelectAllCommand = new RelayCommand(() => SetAll(true));
        ClearAllCommand = new RelayCommand(() => SetAll(false));
        PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => !IsBusy);
    }

    /// <summary>Presets the user can pick.</summary>
    public ObservableCollection<Profile> Profiles { get; }

    /// <summary>Changes grouped by tweak.</summary>
    public ObservableCollection<ChangeGroup> Groups { get; } = [];

    /// <summary>Ticks everything.</summary>
    public RelayCommand SelectAllCommand { get; }

    /// <summary>Unticks everything.</summary>
    public RelayCommand ClearAllCommand { get; }

    /// <summary>Re-runs the preview.</summary>
    public AsyncRelayCommand PreviewCommand { get; }

    /// <summary>The picked preset. Changing it re-previews.</summary>
    public Profile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (Set(ref _selectedProfile, value))
            {
                Raise(nameof(IsCustom));
            }
        }
    }

    /// <summary>
    /// True once the user has unticked something, so the run no longer matches any preset.
    /// Doc 01's third mode: Custom is what you get by editing a preset, not a separate list.
    /// </summary>
    public bool IsCustom => _hasPreviewed && AllRows.Any(row => !row.IsSelected);

    /// <summary>Whether a preview is in flight.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                PreviewCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Why the preview could not finish, or <c>null</c>.</summary>
    public string? Error
    {
        get => _error;
        private set => Set(ref _error, value);
    }

    /// <summary>Whether applying this selection would need a restart.</summary>
    public bool RequiresRestart
    {
        get => _requiresRestart;
        private set => Set(ref _requiresRestart, value);
    }

    /// <summary>Every row across every group.</summary>
    public IReadOnlyList<ChangeRow> AllRows => Groups.SelectMany(group => group.Rows).ToList();

    /// <summary>Rows the user has ticked.</summary>
    public IReadOnlyList<ChangeRow> SelectedRows => AllRows.Where(row => row.IsSelected).ToList();

    /// <summary>How many changes are ticked.</summary>
    public int SelectedCount => SelectedRows.Count;

    /// <summary>How many changes there are in total.</summary>
    public int TotalCount => AllRows.Count;

    /// <summary>Whether Apply should be available.</summary>
    public bool CanApply => !IsBusy && SelectedCount > 0;

    /// <summary>The preview ran and found nothing this profile would change.</summary>
    public bool NothingToDo => _hasPreviewed && TotalCount == 0;

    /// <summary>
    /// There are changes on offer but the user has unticked every one.
    /// </summary>
    /// <remarks>
    /// A different state from <see cref="NothingToDo"/>, and worth saying so: one means the PC has
    /// nothing left to change, the other means the user emptied the list. A disabled Apply button
    /// with no explanation looks like a bug in either case.
    /// </remarks>
    public bool NothingSelected => _hasPreviewed && TotalCount > 0 && SelectedCount == 0;

    /// <summary>Ids of the tweaks the user has left ticked, for the apply step.</summary>
    public IReadOnlyList<string> SelectedTweakIds
        => SelectedRows.Select(row => row.TweakId).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Reads what the selected profile would change. Applies nothing.</summary>
    public async Task PreviewAsync()
    {
        IsBusy = true;
        Error = null;

        try
        {
            Groups.Clear();

            if (SelectedProfile is null)
            {
                return;
            }

            var preview = await _runner.PreviewAsync(_builder.Build(SelectedProfile)).ConfigureAwait(true);

            foreach (var plan in preview.Plans.Where(plan => plan.HasChanges))
            {
                var rows = plan.Changes
                    .Select(change => new ChangeRow(plan.TweakId, plan.TweakName, change, plan.RequiresRestart))
                    .ToList();

                foreach (var row in rows)
                {
                    // A row changing selection changes the counters and the Custom flag.
                    row.PropertyChanged += (_, _) => RaiseSelectionChanged();
                }

                Groups.Add(new ChangeGroup(plan.TweakId, plan.TweakName, rows));
            }

            RequiresRestart = preview.RequiresRestart;
            _hasPreviewed = true;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
            RaiseSelectionChanged();
            Raise(nameof(NothingToDo));
        }
    }

    /// <summary>Ticks or unticks every row in one tweak's group.</summary>
    public void SetGroup(string tweakId, bool selected)
    {
        foreach (var row in AllRows.Where(row => row.TweakId == tweakId))
        {
            row.IsSelected = selected;
        }
    }

    private void SetAll(bool selected)
    {
        foreach (var row in AllRows)
        {
            row.IsSelected = selected;
        }
    }

    private void RaiseSelectionChanged()
    {
        Raise(nameof(SelectedCount));
        Raise(nameof(TotalCount));
        Raise(nameof(CanApply));
        Raise(nameof(IsCustom));
        Raise(nameof(NothingSelected));
        Raise(nameof(SelectedRows));
        Raise(nameof(AllRows));
    }
}
