namespace SystevoTune.Engine.Platform;

/// <summary>One file found by a scan.</summary>
public sealed record FileEntry(string FullPath, long SizeBytes);

/// <summary>
/// Every file read and delete the engine performs. Behind an interface so unit tests never
/// enumerate or delete anything on the real disk.
/// </summary>
public interface IFileSystemService
{
    /// <summary>Whether the directory exists and can be opened.</summary>
    bool DirectoryExists(string path);

    /// <summary>
    /// Files under the path. Must not throw on a folder the user cannot read — skip it and keep
    /// going, because cleanup runs over folders that always contain some locked content.
    /// </summary>
    IEnumerable<FileEntry> EnumerateFiles(string path, bool recursive);

    /// <summary>
    /// Deletes one file. Throws if the file is locked or protected; the caller counts that as
    /// skipped rather than failing the run.
    /// </summary>
    void DeleteFile(string path);
}

/// <summary>
/// The handful of Windows locations the whitelist is allowed to name. Behind an interface so
/// tests resolve tokens to a temp folder instead of the real machine.
/// </summary>
public interface IEnvironmentPaths
{
    /// <summary>The current user's temp folder.</summary>
    string UserTemp { get; }

    /// <summary>Usually C:\Windows.</summary>
    string WindowsDirectory { get; }

    /// <summary>Usually C:\.</summary>
    string SystemDrive { get; }

    /// <summary>The current user's profile root. Used to work out what is off limits.</summary>
    string UserProfile { get; }

    /// <summary>The current user's roaming AppData folder. Holds the per-user Startup folder.</summary>
    string AppData { get; }

    /// <summary>ProgramData. Holds the all-users Startup folder.</summary>
    string ProgramData { get; }
}
