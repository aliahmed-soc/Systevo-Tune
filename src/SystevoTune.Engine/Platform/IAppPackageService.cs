namespace SystevoTune.Engine.Platform;

/// <summary>One installed Store/appx package.</summary>
/// <param name="Name">Short name, e.g. <c>Microsoft.BingNews</c>. What the whitelist matches on.</param>
/// <param name="FullName">Versioned identity, needed to remove it.</param>
/// <param name="InstallLocation">Where its files live. Empty when Windows would not say.</param>
public sealed record AppPackage(string Name, string FullName, string InstallLocation);

/// <summary>
/// Listing and removing Store apps. Doc 3.8 does this through PowerShell, so it sits behind an
/// interface to keep unit tests away from the real package manager.
/// </summary>
public interface IAppPackageService
{
    /// <summary>Packages installed for the current user.</summary>
    Task<IReadOnlyList<AppPackage>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Removes a package for the current user. Throws if Windows refuses.</summary>
    Task RemoveAsync(AppPackage package, CancellationToken cancellationToken);

    /// <summary>
    /// Tries to put a removed package back.
    /// </summary>
    /// <returns><c>true</c> only if the package is installed again afterwards.</returns>
    /// <remarks>
    /// Genuinely best effort, and usually fails. Re-registering needs the original files, which
    /// removal often takes with it — after that only the Microsoft Store can help. Callers must
    /// report a <c>false</c> honestly rather than treating removal as freely reversible.
    /// </remarks>
    Task<bool> TryReinstallAsync(AppPackage package, CancellationToken cancellationToken);
}
