namespace SystevoTune.Engine.Platform.Windows;

/// <summary>
/// The real disk. Thin on purpose — the decisions about what may be deleted live in tested code.
/// </summary>
/// <remarks>Never used by unit tests. Exercised only in the VM procedure of doc 07.</remarks>
public sealed class WindowsFileSystemService : IFileSystemService
{
    /// <inheritdoc />
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc />
    public IEnumerable<FileEntry> EnumerateFiles(string path, bool recursive)
    {
        if (!Directory.Exists(path))
        {
            yield break;
        }

        // IgnoreInaccessible is what keeps a scan of Windows\Temp from dying on the first
        // folder the user cannot open. Locked files still appear here; the delete is what fails.
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = recursive,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        foreach (var file in Directory.EnumerateFiles(path, "*", options))
        {
            FileEntry entry;
            try
            {
                entry = new FileEntry(file, new FileInfo(file).Length);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The file vanished or is unreadable between listing and sizing. Skip it.
                continue;
            }

            yield return entry;
        }
    }

    /// <inheritdoc />
    public void DeleteFile(string path) => File.Delete(path);
}

/// <summary>The real Windows locations.</summary>
/// <remarks>Never used by unit tests.</remarks>
public sealed class WindowsEnvironmentPaths : IEnvironmentPaths
{
    /// <inheritdoc />
    public string UserTemp => Path.TrimEndingDirectorySeparator(Path.GetTempPath());

    /// <inheritdoc />
    public string WindowsDirectory => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    /// <inheritdoc />
    public string SystemDrive => Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))
        ?? throw new InvalidOperationException("Could not work out the system drive.");

    /// <inheritdoc />
    public string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <inheritdoc />
    public string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <inheritdoc />
    public string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
}
