using System.Text.Json;
using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.LaunchPlans;
using TridentCore.Abstractions.Utilities;

namespace TridentCore.Core.Utilities;

public sealed record LaunchPlanSnapshot(IReadOnlyList<LaunchPlanSnapshot.Entry> Entries, string Hash)
{
    public LaunchPlanDocument Document => new() { Operations = Entries.SelectMany(x => x.Document.Operations).ToArray() };

    public IReadOnlyList<LaunchPlanDocument> Documents => Entries.Select(x => x.Document).ToArray();

    public static LaunchPlanSnapshot? LoadOrNull(string key)
    {
        var launchDirectory = PathDef.Default.DirectoryOfLaunch(key);
        if (!Directory.Exists(launchDirectory))
        {
            return null;
        }

        var sourceDirectory = PathDef.Default.DirectoryOfLaunchSource(key);
        var sourceFiles = EnumeratePlanFiles(sourceDirectory);
        var userFiles = Directory.EnumerateFiles(launchDirectory, "*", SearchOption.TopDirectoryOnly)
                                 .Where(LaunchPlanFileHelper.IsEnabledPlanFile)
                                 .OrderBy(x => Path.GetFileName(x), StringComparer.Ordinal)
                                 .ToArray();
        if (sourceFiles.Count == 0 && userFiles.Length == 0)
        {
            return null;
        }

        var entries = sourceFiles.Select(x => Load(x, launchDirectory, true))
                                 .Concat(userFiles.Select(x => Load(x, launchDirectory, false)))
                                 .ToArray();
        var hash = HashHelper.ComputeObjectHash(entries.Select(x => new { x.RelativePath, x.Hash }));
        return new(entries, hash);
    }

    private static IReadOnlyList<string> EnumeratePlanFiles(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                      .Where(LaunchPlanFileHelper.IsEnabledPlanFile)
                      .OrderBy(x => Path.GetFileName(x), StringComparer.Ordinal)
                      .ToArray()
            : [];

    private static Entry Load(string path, string launchDirectory, bool isSource)
    {
        var content = File.ReadAllText(path);
        LaunchPlanDocument document;
        try
        {
            document = JsonSerializer.Deserialize<LaunchPlanDocument>(content, JsonSerializerOptions.Web)
                    ?? throw new FormatException("Launch plan document is empty");
        }
        catch (JsonException e)
        {
            throw new FormatException($"Launch plan '{Path.GetRelativePath(launchDirectory, path)}' is not valid JSON", e);
        }

        var files = new List<(string RelativePath, string Hash)>();
        var resolved = ResolveDocument(document,
                                       Path.GetDirectoryName(Path.GetFullPath(path))!,
                                       launchDirectory,
                                       isSource,
                                       files);
        try
        {
            _ = new LaunchPlan().Apply(resolved);
        }
        catch (FormatException e)
        {
            throw new FormatException($"Launch plan '{Path.GetRelativePath(launchDirectory, path)}' contains an invalid operation", e);
        }

        var hash = HashHelper.ComputeObjectHash(new { Document = document, Files = files });
        return new(Path.GetFullPath(path),
                   Path.GetRelativePath(launchDirectory, path),
                   resolved,
                   document,
                   hash);
    }

    private static LaunchPlanDocument ResolveDocument(
        LaunchPlanDocument document,
        string planDirectory,
        string launchDirectory,
        bool isSource,
        ICollection<(string RelativePath, string Hash)> files)
    {
        var operations = document.Operations.Select(operation => operation switch
        {
            LaunchPlanDocument.AddLibraryOperation add => add with
            {
                Value = ResolveLibrary(add.Value, planDirectory, launchDirectory, isSource, files)
            },
            LaunchPlanDocument.SetAssetIndexOperation set => set with
            {
                Value = ResolveAssetIndex(set.Value, planDirectory, launchDirectory, isSource, files)
            },
            _ => operation
        }).ToArray();
        return new() { Operations = operations };
    }

    private static LockData.Library ResolveLibrary(
        LockData.Library library,
        string planDirectory,
        string launchDirectory,
        bool isSource,
        ICollection<(string RelativePath, string Hash)> files)
    {
        var (url, hash) = ResolveUri(library.Url, planDirectory, launchDirectory, isSource, files);
        return library with { Url = url, Hash = library.Hash ?? hash };
    }

    private static LockData.AssetData ResolveAssetIndex(
        LockData.AssetData asset,
        string planDirectory,
        string launchDirectory,
        bool isSource,
        ICollection<(string RelativePath, string Hash)> files)
    {
        var (url, _) = ResolveUri(asset.Url, planDirectory, launchDirectory, isSource, files);
        return asset with { Url = url };
    }

    private static (Uri Url, FileHash? Hash) ResolveUri(
        Uri uri,
        string planDirectory,
        string launchDirectory,
        bool isSource,
        ICollection<(string RelativePath, string Hash)> files)
    {
        if (uri.IsAbsoluteUri && !uri.IsFile)
        {
            return (uri, null);
        }

        var path = uri.IsAbsoluteUri ? uri.LocalPath : Path.Combine(planDirectory, uri.OriginalString);
        var fullPath = Path.GetFullPath(path);
        if (isSource && !FileHelper.IsInDirectory(fullPath, launchDirectory))
        {
            throw new FormatException($"Launch plan file reference '{uri}' escapes the launch directory");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Launch plan file reference '{uri}' does not exist", fullPath);
        }

        var hash = FileHash.Sha256(FileHelper.ComputeHash(fullPath, HashAlgorithm.Sha256));
        var relative = isSource ? Path.GetRelativePath(launchDirectory, fullPath) : fullPath;
        files.Add((relative, hash.Value));
        return (new Uri(fullPath, UriKind.Absolute), hash);
    }

    public sealed record Entry(
        string Path,
        string RelativePath,
        LaunchPlanDocument Document,
        LaunchPlanDocument SourceDocument,
        string Hash);
}
