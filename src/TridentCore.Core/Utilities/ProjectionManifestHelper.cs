using System.Text.Json;
using TridentCore.Abstractions;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Engines.Deploying;

namespace TridentCore.Core.Utilities;

public static class ProjectionManifestHelper
{
    public const string ALLOWED_SYMLINKS_FILE_NAME = "allowed_symlinks.txt";
    public const string TEMPORARY_DIRECTORY_NAME = ".trident-manifest-tmp";

    public static ImportProjectionManifest ReadImport(string key)
    {
        var path = PathDef.Default.FileOfImportProjectionManifest(key);
        EnsureManifestPath(path, PathDef.Default.DirectoryOfBuild(key));
        return File.Exists(path) ? ReadImportAt(path, PathDef.Default.DirectoryOfBuild(key)) : new();
    }

    public static PersistProjectionManifest ReadPersist(string key)
    {
        var path = PathDef.Default.FileOfPersistProjectionManifest(key);
        EnsureManifestPath(path, PathDef.Default.DirectoryOfBuild(key));
        return File.Exists(path) ? ReadPersistAt(path, PathDef.Default.DirectoryOfBuild(key)) : new();
    }

    public static ImportProjectionManifest ReadImportAt(string path, string build)
    {
        var manifest = JsonSerializer.Deserialize<ImportProjectionManifest>(File.ReadAllText(path), FileHelper.SerializerOptions)
                       ?? throw new InvalidDataException($"Import projection manifest is empty: {path}");
        if (manifest.Version != ImportProjectionManifest.CURRENT_VERSION)
            throw new InvalidDataException($"Unsupported import projection manifest version {manifest.Version}: {path}");
        return manifest with { Files = ValidatePaths(build, manifest.Files) };
    }

    public static PersistProjectionManifest ReadPersistAt(string path, string build)
    {
        var manifest = JsonSerializer.Deserialize<PersistProjectionManifest>(File.ReadAllText(path), FileHelper.SerializerOptions)
                       ?? throw new InvalidDataException($"Persist projection manifest is empty: {path}");
        if (manifest.Version != PersistProjectionManifest.CURRENT_VERSION)
            throw new InvalidDataException($"Unsupported persist projection manifest version {manifest.Version}: {path}");
        var paths = ValidatePaths(build, manifest.Files.Select(x => x.Path));
        var entries = manifest.Files.Zip(paths, (entry, normalized) => entry with { Path = normalized }).ToArray();
        return manifest with { Files = entries };
    }

    public static async Task WriteAsync(
        string key,
        ImportProjectionManifest imports,
        PersistProjectionManifest persists,
        CancellationToken token)
    {
        var build = PathDef.Default.DirectoryOfBuild(key);
        Directory.CreateDirectory(build);
        if (DeploymentFileHelper.LinkTarget(build) is not null)
            throw new InvalidDataException($"The run directory cannot be a symbolic link: {build}");
        await WriteAtomicAsync(build, PathDef.Default.FileOfImportProjectionManifest(key), imports, token).ConfigureAwait(false);
        await WriteAtomicAsync(build, PathDef.Default.FileOfPersistProjectionManifest(key), persists, token).ConfigureAwait(false);
    }

    public static void DeleteImport(string key) =>
        Delete(PathDef.Default.FileOfImportProjectionManifest(key), PathDef.Default.DirectoryOfBuild(key));

    public static void DeletePersist(string key) =>
        Delete(PathDef.Default.FileOfPersistProjectionManifest(key), PathDef.Default.DirectoryOfBuild(key));

    public static void DeleteAll(string key)
    {
        DeleteImport(key);
        DeletePersist(key);
    }

    public static string ToStoredPath(string build, string path)
    {
        var relative = Path.GetRelativePath(build, path);
        _ = DeploymentFileHelper.ProjectionPath(build, relative);
        return relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    public static string ResolveStoredPath(string build, string path)
    {
        ValidateStoredPath(path);
        return DeploymentFileHelper.ProjectionPath(build, path.Replace('/', Path.DirectorySeparatorChar));
    }

    public static bool IsReservedProjectionPath(string build, string target)
    {
        var reserved = new[]
        {
            Path.Combine(build, "trident.import.json"),
            Path.Combine(build, "trident.persist.json"),
            Path.Combine(build, ALLOWED_SYMLINKS_FILE_NAME),
            Path.Combine(build, TEMPORARY_DIRECTORY_NAME),
            Path.Combine(build, "natives")
        };
        return reserved.Any(path => FileHelper.IsPathEquivalent(target, path) || FileHelper.IsInDirectory(target, path));
    }

    private static IReadOnlyList<string> ValidatePaths(string build, IEnumerable<string> paths)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(FileHelper.PathComparer);
        foreach (var path in paths)
        {
            var resolved = ResolveStoredPath(build, path);
            if (IsReservedProjectionPath(build, resolved))
                throw new InvalidDataException($"Projection manifest contains a reserved path: {path}");
            var normalized = ToStoredPath(build, resolved);
            if (!seen.Add(normalized)) throw new InvalidDataException($"Projection manifest contains a duplicate path: {path}");
            result.Add(normalized);
        }
        return result;
    }

    private static void ValidateStoredPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\'))
            throw new InvalidDataException($"Invalid projection manifest path: {path}");
        var parts = path.Split('/');
        if (parts.Any(x => x.Length == 0 || x is "." or ".."))
            throw new InvalidDataException($"Invalid projection manifest path: {path}");
    }

    private static void Delete(string path, string build)
    {
        EnsureManifestPath(path, build);
        File.Delete(path);
    }

    private static void EnsureManifestPath(string path, string build)
    {
        if (DeploymentFileHelper.LinkTarget(build) is not null)
            throw new InvalidDataException($"The run directory cannot be a symbolic link: {build}");
        if (Directory.Exists(path))
            throw new InvalidDataException($"Projection manifest path is occupied by a directory: {path}");
        if (DeploymentFileHelper.LinkTarget(path) is not null)
            throw new InvalidDataException($"Projection manifest cannot be a symbolic link: {path}");
    }

    private static async Task WriteAtomicAsync<T>(string build, string path, T value, CancellationToken token)
    {
        EnsureManifestPath(path, build);
        var temporaryDirectory = Path.Combine(build, TEMPORARY_DIRECTORY_NAME);
        var temporary = Path.Combine(temporaryDirectory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        DeploymentFileHelper.EnsureRealParent(temporary, build);
        try
        {
            await using (var stream = File.Create(temporary))
                await JsonSerializer.SerializeAsync(stream, value, FileHelper.SerializerOptions, token).ConfigureAwait(false);
            File.Move(temporary, path, true);
        }
        finally
        {
            File.Delete(temporary);
            if (Directory.Exists(temporaryDirectory) && !Directory.EnumerateFileSystemEntries(temporaryDirectory).Any())
                Directory.Delete(temporaryDirectory, false);
        }
    }
}
