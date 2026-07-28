using System.Windows;
using System.Windows.Controls;

namespace SystevoTune.App.Views;

/// <summary>Screen 4. Summary, Undo All, and re-apply.</summary>
public partial class ResultsView : UserControl
{
    /// <summary>Creates the view.</summary>
    public ResultsView() => InitializeComponent();

    /// <summary>
    /// Hands re-apply to the window, which runs the confirm dialog first.
    /// </summary>
    /// <remarks>
    /// Same reasoning as Apply on the Review screen: it needs a modal dialog and an owner window,
    /// and putting either in a view model is what makes view models untestable.
    /// </remarks>
    private async void OnReapplyClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow window)
        {
            await window.ReapplyLastAsync().ConfigureAwait(true);
        }
    }
}
