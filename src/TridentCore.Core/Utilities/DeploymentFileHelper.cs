using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Engines.Deploying;

namespace TridentCore.Core.Utilities;

public static class DeploymentFileHelper
{
    public static string ProjectionPath(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new InvalidDataException($"Projection path must be relative: {relative}");
        var target = Path.GetFullPath(Path.Combine(root, relative));
        if (FileHelper.IsPathEquivalent(target, root) || !FileHelper.IsInDirectory(target, root))
            throw new InvalidDataException($"Projection path escapes the run directory: {relative}");
        return target;
    }

    public static void RequireFile(DeploymentTarget target, string path, Uri? url, FileHash? hash, bool executable = false)
    {
        var existing = target.Downloads.FirstOrDefault(x => FileHelper.IsPathEquivalent(x.Path, path));
        if (existing is not null)
        {
            if (existing.Hash != hash || existing.Url != url) throw new InvalidDataException($"Conflicting file requirements: {path}");
            if (executable && !existing.Executable)
                target.Downloads[target.Downloads.IndexOf(existing)] = existing with { Executable = true };
            return;
        }
        target.Downloads.Add(new(path, url!, hash, executable));
    }

    public static string? LinkTarget(string path) => new FileInfo(path).LinkTarget;

    public static bool LinkMatches(string path, string target)
    {
        var current = LinkTarget(path);
        return current is not null && FileHelper.IsPathEquivalent(Path.GetFullPath(current, Path.GetDirectoryName(path)!), target);
    }

    public static bool HasLinkAtOrAbove(string path, string root)
    {
        var current = path;
        while (!FileHelper.IsPathEquivalent(current, root))
        {
            if (LinkTarget(current) is not null) return true;
            current = Path.GetDirectoryName(current) ?? throw new InvalidDataException($"Path escapes managed root: {path}");
        }
        return LinkTarget(root) is not null;
    }

    public static IEnumerable<string> EnumerateFilesWithoutLinks(string root)
    {
        if (LinkTarget(root) is not null)
            throw new InvalidDataException($"Managed source directory cannot be a symbolic link: {root}");
        if (!Directory.Exists(root)) yield break;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var current))
        {
            foreach (var entry in new DirectoryInfo(current).EnumerateFileSystemInfos())
            {
                if (entry.LinkTarget is not null)
                    throw new InvalidDataException($"Managed source cannot contain a symbolic link: {entry.FullName}");
                if (entry is DirectoryInfo directory) pending.Push(directory.FullName);
                else yield return entry.FullName;
            }
        }
    }

    public static void EnsureRealParent(string path, string root)
    {
        if (!FileHelper.IsInDirectory(path, root)) throw new InvalidDataException($"Path escapes managed root: {path}");
        if (LinkTarget(root) is not null) throw new InvalidDataException($"Managed root cannot be a symbolic link: {root}");
        Directory.CreateDirectory(root);
        var relative = Path.GetRelativePath(root, Path.GetDirectoryName(path)!);
        var current = root;
        foreach (var segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            DeleteLink(current);
            if (File.Exists(current)) throw new IOException($"A file occupies a required directory: {current}");
            Directory.CreateDirectory(current);
        }
    }

    public static bool DeleteLink(string path)
    {
        if (LinkTarget(path) is null) return false;
        if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) Directory.Delete(path, false);
        else File.Delete(path);
        return true;
    }

    public static bool DirectoryContainsOnlyLinksAndEmptyDirectories(string path) =>
        DirectoryContainsOnlyLinksEmptyDirectoriesOrFiles(path, new HashSet<string>(FileHelper.PathComparer));

    public static bool DirectoryContainsOnlyLinksEmptyDirectoriesOrFiles(string path, ISet<string> allowedFiles)
    {
        if (!Directory.Exists(path)) return true;
        foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            if (entry.LinkTarget is not null) continue;
            if (entry is DirectoryInfo directory)
            {
                if (!DirectoryContainsOnlyLinksEmptyDirectoriesOrFiles(directory.FullName, allowedFiles)) return false;
                continue;
            }
            if (!allowedFiles.Contains(entry.FullName)) return false;
        }
        return true;
    }

    public static bool DeleteDirectoryTreeIfEmptyOrLinks(string path, string root)
    {
        if (!FileHelper.IsInDirectory(path, root)) throw new InvalidDataException($"Path escapes managed root: {path}");
        if (!DirectoryContainsOnlyLinksAndEmptyDirectories(path)) return false;
        return DeleteDirectoryTreeIfEmptyOrLinks(path);
    }

    public static void TrimEmptyParents(string root, string? current)
    {
        while (current is not null && !FileHelper.IsPathEquivalent(current, root))
        {
            if (!FileHelper.IsInDirectory(current, root))
                throw new InvalidDataException($"Path escapes managed root: {current}");
            if (!Directory.Exists(current) || Directory.EnumerateFileSystemEntries(current).Any()) return;
            Directory.Delete(current, false);
            current = Path.GetDirectoryName(current);
        }
    }

    public static IEnumerable<(string Path, bool Directory)> EnumerateLinks(string root, CancellationToken token)
    {
        if (!Directory.Exists(root)) yield break;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                if (entry.LinkTarget is not null)
                    yield return (entry.FullName, (entry.Attributes & FileAttributes.Directory) != 0);
                else if (entry is DirectoryInfo) pending.Push(entry.FullName);
            }
        }
    }

    public static void DeleteAllLinks(string root, CancellationToken token)
    {
        if (LinkTarget(root) is not null)
            throw new InvalidDataException($"Managed root cannot be a symbolic link: {root}");
        foreach (var link in EnumerateLinks(root, token).OrderByDescending(x => x.Path.Length))
        {
            token.ThrowIfCancellationRequested();
            DeleteLink(link.Path);
            TrimEmptyParents(root, Path.GetDirectoryName(link.Path));
        }
    }

    private static bool DeleteDirectoryTreeIfEmptyOrLinks(string path)
    {
        if (!Directory.Exists(path)) return true;
        foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            if (entry.LinkTarget is not null)
            {
                DeleteLink(entry.FullName);
                continue;
            }
            if (entry is not DirectoryInfo directory || !DeleteDirectoryTreeIfEmptyOrLinks(directory.FullName))
                return false;
        }
        Directory.Delete(path, false);
        return true;
    }
}
