using System.Text.Json;
using TridentCore.Abstractions;
using TridentCore.Abstractions.Launching;
using TridentCore.Abstractions.Utilities;
using FileHash = TridentCore.Abstractions.Utilities.FileHash;
using HashAlgorithm = TridentCore.Abstractions.Utilities.HashAlgorithm;

namespace TridentCore.Core.Utilities;

public sealed record LaunchDefinitionSnapshot(
    LaunchDefinition? Definition,
    IReadOnlyDictionary<string, LaunchDefinitionSnapshot.Entry> Components,
    string Fingerprint)
{
    public static LaunchDefinitionSnapshot Load(string key, bool includeUser = true) =>
        LoadDirectory(PathDef.Default.DirectoryOfLaunch(key), includeUser);

    public static LaunchDefinitionSnapshot LoadDirectory(string directory, bool includeUser = true)
    {
        LaunchDefinition? definition = null;
        var components = new Dictionary<string, Entry>(StringComparer.Ordinal);
        var files = new List<TreeFile>();
        foreach (var layer in includeUser
                     ? new[] { LaunchDefinitionHelper.IMPORT_DIRECTORY, LaunchDefinitionHelper.USER_DIRECTORY }
                     : new[] { LaunchDefinitionHelper.IMPORT_DIRECTORY })
        {
            var root = Path.GetFullPath(Path.Combine(directory, layer));
            if (!Directory.Exists(root))
            {
                continue;
            }
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                files.Add(new(Path.GetRelativePath(directory, file).Replace('\\', '/'),
                              FileHelper.ComputeHash(file, HashAlgorithm.Sha256)));
            }
            var manifest = Path.Combine(root, LaunchDefinitionHelper.DEFINITION_FILE);
            if (File.Exists(manifest))
            {
                definition = LaunchDefinitionHelper.Deserialize<LaunchDefinition>(File.ReadAllText(manifest), manifest);
                if (definition.FormatVersion != 1)
                {
                    throw new FormatException($"Unsupported launch definition format in '{manifest}'");
                }
            }
            var componentDirectory = Path.Combine(root, LaunchDefinitionHelper.COMPONENT_DIRECTORY);
            if (!Directory.Exists(componentDirectory))
            {
                continue;
            }
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(componentDirectory, "*.json").Order(StringComparer.Ordinal))
            {
                var component = LaunchDefinitionHelper.Deserialize<LaunchComponent>(File.ReadAllText(file), file);
                LaunchDefinitionHelper.Validate(component, file);
                if (!identities.Add(component.Id))
                {
                    throw new FormatException($"Duplicate component '{component.Id}' in '{root}'");
                }
                components[component.Id] = new(component, Path.GetFullPath(file), root,
                                               layer == LaunchDefinitionHelper.IMPORT_DIRECTORY);
            }
        }
        return new(definition, components,
                    HashHelper.ComputeObjectHash(files.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray()));
    }

    public ResolvedLaunchComponent Resolve(Entry entry)
    {
        var definition = entry.Definition;
        return new(definition with
        {
            Libraries = definition.Libraries.Select(library =>
            {
                var (url, hash) = ResolveFile(library.Artifact.Url, library.Artifact.Hash, entry);
                return library with { Artifact = library.Artifact with { Url = url, Hash = hash } };
            }).ToArray(),
            AssetIndex = definition.AssetIndex is { } index ? ResolveAsset(index, entry) : null
        }, entry.Path);
    }

    private static LaunchAssetIndex ResolveAsset(LaunchAssetIndex asset, Entry entry)
    {
        var (url, hash) = ResolveFile(asset.Url, asset.Hash, entry);
        return asset with { Url = url, Hash = hash };
    }

    private static (Uri Url, FileHash? Hash) ResolveFile(Uri uri, FileHash? hash, Entry entry)
    {
        if (uri.IsAbsoluteUri && !uri.IsFile)
        {
            return (uri, hash);
        }
        var path = Path.GetFullPath(uri.IsAbsoluteUri ? uri.LocalPath
                                  : Path.Combine(Path.GetDirectoryName(entry.Path)!, uri.OriginalString));
        if (entry.IsImported)
        {
            if (!FileHelper.IsInDirectory(path, entry.Root))
            {
                throw new FormatException($"Component '{entry.Definition.Id}' references a file outside its import directory: '{uri}'");
            }
            for (var current = path; !FileHelper.IsPathEquivalent(current, entry.Root); current = Path.GetDirectoryName(current)!)
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new FormatException($"Imported component reference '{uri}' traverses a symbolic link");
                }
            }
        }
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Component '{entry.Definition.Id}' references missing file '{uri}'", path);
        }
        if (hash is not null && !FileHelper.VerifyModified(path, null, hash))
        {
            throw new FormatException($"Component '{entry.Definition.Id}' references a file with an invalid hash: '{uri}'");
        }
        return (new Uri(path, UriKind.Absolute), FileHash.Sha256(FileHelper.ComputeHash(path, HashAlgorithm.Sha256)));
    }

    public sealed record Entry(LaunchComponent Definition, string Path, string Root, bool IsImported);
    private sealed record TreeFile(string Path, string Hash);
}
