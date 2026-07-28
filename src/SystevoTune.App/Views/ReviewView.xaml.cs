using System.Windows;
using System.Windows.Controls;

namespace SystevoTune.App.Views;

/// <summary>Screen 2. Nothing here changes the system.</summary>
public partial class ReviewView : UserControl
{
    /// <summary>Creates the view.</summary>
    public ReviewView() => InitializeComponent();

    /// <summary>
    /// Hands the apply decision to the window, which runs the confirm dialog first.
    /// </summary>
    /// <remarks>
    /// Deliberately not a command on the view model. Applying needs a modal dialog and an owner
    /// window, and putting either in a view model is what makes view models untestable.
    /// </remarks>
    private async void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow window)
        {
            await window.ApplySelectedAsync().ConfigureAwait(true);
        }
    }
}
