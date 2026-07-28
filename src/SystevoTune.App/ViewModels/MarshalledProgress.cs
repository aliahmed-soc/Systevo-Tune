namespace SystevoTune.App.ViewModels;

/// <summary>
/// Progress reporting that lands on the UI thread under WPF and runs inline everywhere else.
/// </summary>
/// <remarks>
/// <see cref="Progress{T}"/> looked like the obvious choice and is subtly wrong here. It captures
/// the <see cref="SynchronizationContext"/> at construction, and when there is none — a unit test,
/// or any non-UI host — it falls back to the <b>thread pool</b>. The engine reports progress from
/// inside <c>ConfigureAwait(false)</c> continuations, so several callbacks can then land on
/// different pool threads at once and race while appending to an <c>ObservableCollection</c>.
/// <para>
/// This version keeps the WPF behaviour (post to the captured context, so bindings update on the
/// dispatcher thread) and replaces the thread-pool fallback with a plain inline call, which is
/// both safe and deterministic.
/// </para>
/// </remarks>
/// <param name="handler">What to run for each report.</param>
/// <param name="context">
/// Where to run it. The UI dispatcher context under WPF; <c>null</c> to run inline, which is what
/// tests pass so the reports land in a known order rather than whenever a pool thread gets to them.
/// </param>
public sealed class MarshalledProgress<T>(Action<T> handler, SynchronizationContext? context) : IProgress<T>
{
    /// <inheritdoc />
    public void Report(T value)
    {
        if (context is null)
        {
            handler(value);
            return;
        }

        context.Post(state => handler((T)state!), value);
    }
}
