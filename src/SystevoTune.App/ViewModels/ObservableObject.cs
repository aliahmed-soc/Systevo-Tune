using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace SystevoTune.App.ViewModels;

/// <summary>
/// Change notification for view models.
/// </summary>
/// <remarks>
/// Hand-rolled rather than pulled from a MVVM package. It is thirty lines, and doc 02 keeps the
/// dependency list short on purpose for a tool that has to be trusted with system settings —
/// every package is something a reviewer has to take on faith.
/// </remarks>
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raises change notification for a property.</summary>
    protected void Raise([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Sets a field and notifies, returning whether the value actually moved.</summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(propertyName);
        return true;
    }
}

/// <summary>
/// An <see cref="ICommand"/> over a delegate.
/// </summary>
/// <remarks>
/// <see cref="RaiseCanExecuteChanged"/> is explicit rather than routed through
/// <c>CommandManager</c>, because <c>CommandManager</c> needs a running WPF dispatcher and these
/// view models are unit tested without one.
/// </remarks>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    /// <inheritdoc />
    public void Execute(object? parameter) => execute();

    /// <summary>Tells the UI to re-ask <see cref="CanExecute"/>.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>An <see cref="ICommand"/> over an async delegate, with re-entry blocked while it runs.</summary>
public sealed class AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool _running;

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <summary>Whether the command is mid-flight. Bound to show a busy state.</summary>
    public bool IsRunning => _running;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke() ?? true);

    /// <inheritdoc />
    public async void Execute(object? parameter) => await ExecuteAsync().ConfigureAwait(true);

    /// <summary>Runs the command and awaits it. Tests use this rather than <see cref="Execute"/>.</summary>
    public async Task ExecuteAsync()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        RaiseCanExecuteChanged();

        try
        {
            await execute().ConfigureAwait(true);
        }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }

    /// <summary>Tells the UI to re-ask <see cref="CanExecute"/>.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
