using System.Text.Json;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Importers;
using TridentCore.Abstractions.Launching;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Models.MultiMcPack;
using TridentCore.Core.Models.PrismLauncherApi;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Importers;

public class MultiMcImporter : IProfileImporter
{
    public bool CanHandle(CompressedProfilePack pack) => pack.FileNames.Contains(MultiMcHelper.PACK_INDEX_FILE_NAME);

    public async Task<ImportedProfileContainer> ExtractAsync(CompressedProfilePack pack)
    {
        await using var indexStream = pack.Open(MultiMcHelper.PACK_INDEX_FILE_NAME);
        var index = await JsonSerializer.DeserializeAsync<MmcPack>(indexStream, JsonSerializerOptions.Web).ConfigureAwait(false)
                    ?? throw new FormatException("Invalid mmc-pack.json");
        if (index.FormatVersion != 1) throw new FormatException($"Unsupported MMC pack format {index.FormatVersion}");
        var active = index.Components.Where(x => !x.Disabled).ToArray();
        var minecraftVersion = active.FirstOrDefault(x => x.Uid == MultiMcHelper.UID_MINECRAFT)?.Version
                               ?? throw new FormatException("MMC pack has no active Minecraft version");
        var loaders = active.Where(x => MultiMcHelper.UidToLoaderMappings.ContainsKey(x.Uid)).ToArray();
        if (loaders.Length > 1) throw new NotSupportedException("Multiple active mod loaders cannot be represented by the instance profile");
        var loader = loaders.FirstOrDefault();
        var profile = new Profile
        {
            Name = await ReadNameAsync(pack).ConfigureAwait(false) ?? "Imported MultiMc Pack",
            Setup = new()
            {
                Version = minecraftVersion,
                Loader = loader is null ? null : LoaderHelper.ToLurl(MultiMcHelper.UidToLoaderMappings[loader.Uid],
                    loader.Version ?? throw new FormatException("MMC loader has no version")),
                Packages = []
            }
        };

        var selections = new List<LaunchSelection>();
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var generated = new List<(string Target, byte[] Content)>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var selection in index.Components)
        {
            var componentFile = LaunchDefinitionHelper.ComponentFile(selection.Uid);
            if (!identities.Add(selection.Uid)) throw new FormatException($"Duplicate MMC component '{selection.Uid}'");
            selections.Add(selection.Uid == MultiMcHelper.UID_MINECRAFT
                ? new() { From = LaunchSelection.Binding.Minecraft, Enabled = !selection.Disabled }
                : selection.Uid == loader?.Uid
                    ? new() { From = LaunchSelection.Binding.Loader, Enabled = !selection.Disabled }
                    : new() { Id = selection.Uid, Version = selection.Version, Enabled = !selection.Disabled });

            var patchPath = $"patches/{selection.Uid}.json";
            if (!pack.FileNames.Contains(patchPath)) continue;
            await using var stream = pack.Open(patchPath);
            var patch = await JsonSerializer.DeserializeAsync<Component>(stream, JsonSerializerOptions.Web).ConfigureAwait(false)
                        ?? throw new FormatException($"Invalid MMC component '{patchPath}'");
            var conversion = MetadataComponentHelper.Convert(patch, selection.Uid, selection.Version, minecraftVersion);
            generated.Add((LaunchDefinitionHelper.IMPORT_PREFIX + componentFile,
                           LaunchDefinitionHelper.Serialize(conversion.Component)));
            foreach (var local in conversion.LocalFiles)
            {
                var archivePath = pack.FileNames.Contains(local.ArchivePath) ? local.ArchivePath
                    : $"{MultiMcHelper.PACK_MINECRAFT_DIR}/{local.ArchivePath}";
                if (!pack.FileNames.Contains(archivePath))
                {
                    throw new FileNotFoundException($"MMC component '{selection.Uid}' references missing file '{local.ArchivePath}'");
                }
                var target = LaunchDefinitionHelper.IMPORT_PREFIX + local.Target;
                if (files.TryGetValue(target, out var previous) && previous != archivePath)
                {
                    throw new FormatException($"MMC libraries collide at '{target}'");
                }
                files[target] = archivePath;
            }
        }
        generated.Add((LaunchDefinitionHelper.IMPORT_PREFIX + LaunchDefinitionHelper.DEFINITION_FILE,
                       LaunchDefinitionHelper.Serialize(new LaunchDefinition { Components = selections })));
        var minecraftPrefix = MultiMcHelper.PACK_MINECRAFT_DIR + "/";
        var importFiles = pack.FileNames.Where(x => x.StartsWith(minecraftPrefix, StringComparison.Ordinal))
            .Select(x => (x, x[minecraftPrefix.Length..]))
            .Where(x => ZipArchiveHelper.IsExtractableEntry(x.Item2)).ToArray();
        return new(profile, importFiles, [("pack.png", "icon.png")], null,
                   files.Select(x => (x.Value, x.Key)).ToArray(), generated, [], true);
    }

    private static async Task<string?> ReadNameAsync(CompressedProfilePack pack)
    {
        if (!pack.FileNames.Contains(MultiMcHelper.PACK_INSTANCE_CFG)) return null;
        await using var stream = pack.Open(MultiMcHelper.PACK_INSTANCE_CFG);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (line.StartsWith("name=", StringComparison.OrdinalIgnoreCase)) return line[5..];
        }
        return null;
    }
}
