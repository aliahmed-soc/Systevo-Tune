using SystevoTune.Engine.Platform;

namespace SystevoTune.TestSupport;

/// <summary>
/// An in-memory disk. The only file system any unit test is allowed to touch â€” nothing here
/// reaches a real path.
/// </summary>
public sealed class FakeFileSystem : IFileSystemService
{
    private readonly Dictionary<string, long> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Files whose delete throws, standing in for in-use files.</summary>
    public HashSet<string> LockedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Directories that exist but throw when walked.</summary>
    public HashSet<string> UnreadableDirectories { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Paths passed to <see cref="DeleteFile"/>, in order.</summary>
    public List<string> Deleted { get; } = [];

    /// <summary>Adds a file and every directory above it.</summary>
    public FakeFileSystem WithFile(string fullPath, long sizeBytes)
    {
        _files[fullPath] = sizeBytes;

        var directory = Path.GetDirectoryName(fullPath);
        while (!string.IsNullOrEmpty(directory))
        {
            _directories.Add(Path.TrimEndingDirectorySeparator(directory));
            directory = Path.GetDirectoryName(directory);
        }

        return this;
    }

    /// <summary>Adds an empty directory.</summary>
    public FakeFileSystem WithDirectory(string path)
    {
        _directories.Add(Path.TrimEndingDirectorySeparator(path));
        return this;
    }

    /// <summary>Whether a file is still there.</summary>
    public bool Exists(string fullPath) => _files.ContainsKey(fullPath);

    /// <inheritdoc />
    public bool DirectoryExists(string path) => _directories.Contains(Path.TrimEndingDirectorySeparator(path));

    /// <inheritdoc />
    public IEnumerable<FileEntry> EnumerateFiles(string path, bool recursive)
    {
        var root = Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar;

        foreach (var (fullPath, size) in _files)
        {
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var directory = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(fullPath)!);

            // A folder the user cannot open is skipped, matching IgnoreInaccessible on the real one.
            if (UnreadableDirectories.Any(blocked =>
                    directory.StartsWith(Path.TrimEndingDirectorySeparator(blocked), StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (!recursive && !string.Equals(directory, Path.TrimEndingDirectorySeparator(path), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return new FileEntry(fullPath, size);
        }
    }

    /// <inheritdoc />
    public void DeleteFile(string path)
    {
        Deleted.Add(path);

        if (LockedFiles.Contains(path))
        {
            throw new IOException($"'{path}' is in use by another process.");
        }

        _files.Remove(path);
    }
}

/// <summary>Windows locations pointed at made-up absolute paths. Nothing real is touched.</summary>
public sealed class FakeEnvironmentPaths : IEnvironmentPaths
{
    public string UserTemp { get; init; } = @"C:\FakeUsers\tester\AppData\Local\Temp";

    public string WindowsDirectory { get; init; } = @"C:\FakeWindows";

    public string SystemDrive { get; init; } = @"C:\";

    public string UserProfile { get; init; } = @"C:\FakeUsers\tester";

    public string AppData { get; init; } = @"C:\FakeUsers\tester\AppData\Roaming";

    public string ProgramData { get; init; } = @"C:\FakeProgramData";
}
