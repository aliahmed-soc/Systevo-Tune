using System.Windows;
using SystevoTune.App.Localization;
using SystevoTune.App.Services;

namespace SystevoTune.App;

/// <summary>Application entry point.</summary>
public partial class App : Application
{
    /// <summary>Builds the engine and shows the window.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var localizer = new Localizer();

        // A failure here means a whitelist or profile file did not load, which is a build
        // problem rather than something the user can act on — say so plainly and stop, rather
        // than opening a window that cannot do anything.
        AppEngine engine;
        try
        {
            engine = AppEngine.Create();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                localizer["App_Title"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
            return;
        }

        MainWindow = new MainWindow(engine, localizer);
        MainWindow.Show();
    }
}
