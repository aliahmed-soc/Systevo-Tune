using SystevoTune.Engine.Safety;

namespace SystevoTune.TestSupport;

/// <summary>
/// A throwaway log directory under the system temp folder. Never C:\ProgramData â€”
/// tests must not touch the real log location or any real system state.
/// </summary>
public sealed class TempLogDirectory : IDisposable
{
    public TempLogDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "SystevoTune.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a test over.
        }
    }
}

/// <summary>A clock that only moves when a test moves it. Local time is UTC so ids are stable.</summary>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public void Advance(TimeSpan amount) => _now = _now.Add(amount);
}

/// <summary>Stands in for the machine: target name to current value. Nothing here touches Windows.</summary>
public sealed class FakeSystem
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

    public string? Get(string target) => _values.GetValueOrDefault(target);

    public void Set(string target, string? value) => _values[target] = value;
}

/// <summary>Restores old values into a <see cref="FakeSystem"/> and remembers the order it was asked.</summary>
public sealed class FakeUndoHandler(FakeSystem system, string module = "Fake") : IUndoHandler
{
    private readonly List<string> _undoneIds = [];

    public string Module { get; } = module;

    /// <summary>Record ids in the order undo reached them.</summary>
    public IReadOnlyList<string> UndoneIds => _undoneIds;

    /// <summary>Targets that throw instead of restoring, to exercise partial failure.</summary>
    public HashSet<string> FailingTargets { get; } = new(StringComparer.Ordinal);

    public Task UndoAsync(ChangeRecord record, CancellationToken cancellationToken)
    {
        if (FailingTargets.Contains(record.Target))
        {
            throw new InvalidOperationException($"'{record.Target}' is locked by another process.");
        }

        system.Set(record.Target, record.OldValue);
        _undoneIds.Add(record.Id);
        return Task.CompletedTask;
    }
}
