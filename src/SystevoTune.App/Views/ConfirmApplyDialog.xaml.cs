using System.ComponentModel;
using System.Windows;
using SystevoTune.App.Localization;
using SystevoTune.App.ViewModels;

namespace SystevoTune.App.Views;

/// <summary>
/// The confirm step between Review and Apply (A6).
/// </summary>
/// <remarks>
/// The restore point is attempted while this dialog is open, before the user can confirm, so the
/// decision is made with the answer already on screen.
/// </remarks>
public partial class ConfirmApplyDialog : Window
{
    private readonly ConfirmApplyViewModel _model;
    private readonly ILocalizer _localizer;

    /// <summary>Creates the dialog.</summary>
    public ConfirmApplyDialog(ConfirmApplyViewModel model, ILocalizer localizer)
    {
        _model = model;
        _localizer = localizer;
        DataContext = model;

        InitializeComponent();

        FlowDirection = localizer.FlowDirection;
        model.PropertyChanged += OnModelChanged;

        Loaded += async (_, _) => await model.PrepareAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// The warning text is set in code rather than bound, because the view model deliberately
    /// exposes a resource <em>key</em> rather than English prose — the message the user reads has
    /// to be translated like every other string.
    /// </summary>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConfirmApplyViewModel.WarningKey) or nameof(ConfirmApplyViewModel.Result))
        {
            WarningText.Text = _model.WarningKey is { } key ? _localizer[key] : string.Empty;
        }
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        _model.Confirm();
        DialogResult = true;
    }
}
