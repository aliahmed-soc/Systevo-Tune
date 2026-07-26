using SystevoTune.Engine.Platform;

namespace SystevoTune.TestSupport;

/// <summary>
/// In-memory Store apps. No unit test runs Get-AppxPackage or removes anything real.
/// </summary>
public sealed class FakeAppPackageService : IAppPackageService
{
    private readonly Dictionary<string, AppPackage> _installed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Packages whose removal throws, as Windows sometimes does.</summary>
    public HashSet<string> RefusesToRemove { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Packages whose files survived removal, so re-registering works. Empty by default, because
    /// on a real PC it usually does not.
    /// </summary>
    public HashSet<string> CanReinstall { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Names passed to <see cref="RemoveAsync"/>, in order.</summary>
    public List<string> Removed { get; } = [];

    /// <summary>Adds an installed package.</summary>
    public FakeAppPackageService With(string name, string? fullName = null)
    {
        _installed[name] = new AppPackage(name, fullName ?? $"{name}_1.0_x64__test", $@"C:\FakeApps\{name}");
        return this;
    }

    /// <summary>Whether the package is installed now.</summary>
    public bool IsInstalled(string name) => _installed.ContainsKey(name);

    public Task<IReadOnlyList<AppPackage>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<AppPackage>>(_installed.Values.ToList());

    public Task RemoveAsync(AppPackage package, CancellationToken cancellationToken)
    {
        if (RefusesToRemove.Contains(package.Name))
        {
            return Task.FromException(
                new InvalidOperationException($"Windows would not remove '{package.Name}'."));
        }

        Removed.Add(package.Name);
        _installed.Remove(package.Name);
        return Task.CompletedTask;
    }

    public Task<bool> TryReinstallAsync(AppPackage package, CancellationToken cancellationToken)
    {
        if (!CanReinstall.Contains(package.Name))
        {
            return Task.FromResult(false);
        }

        _installed[package.Name] = package with { InstallLocation = $@"C:\FakeApps\{package.Name}" };
        return Task.FromResult(true);
    }
}
