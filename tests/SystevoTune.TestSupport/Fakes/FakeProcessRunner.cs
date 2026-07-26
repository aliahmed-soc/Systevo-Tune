using SystevoTune.Engine.Platform;

namespace SystevoTune.TestSupport;

/// <summary>
/// Stands in for running an external program. No unit test may start a real process.
/// </summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    private ProcessResult _result = new(0, string.Empty, string.Empty);
    private Exception? _failure;

    /// <summary>Every invocation, as <c>fileName arg arg ...</c>.</summary>
    public List<string> Invocations { get; } = [];

    /// <summary>The arguments of the most recent invocation.</summary>
    public IReadOnlyList<string> LastArguments { get; private set; } = [];

    /// <summary>Makes the next runs return this result.</summary>
    public FakeProcessRunner Returning(int exitCode, string standardOutput = "", string standardError = "")
    {
        _result = new ProcessResult(exitCode, standardOutput, standardError);
        return this;
    }

    /// <summary>Makes the next runs throw, as if the program could not be started.</summary>
    public FakeProcessRunner Throwing(Exception failure)
    {
        _failure = failure;
        return this;
    }

    /// <inheritdoc />
    public Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Invocations.Add(string.Join(' ', [fileName, .. arguments]));
        LastArguments = arguments;

        return _failure is not null ? Task.FromException<ProcessResult>(_failure) : Task.FromResult(_result);
    }
}
