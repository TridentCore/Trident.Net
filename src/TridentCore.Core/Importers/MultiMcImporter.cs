using System.Text.Json;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Importers;
using TridentCore.Abstractions.LaunchPlans;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Models.MultiMcPack;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Importers;

public class MultiMcImporter : IProfileImporter
{
    #region IProfileImporter Members

    public bool CanHandle(CompressedProfilePack pack) =>
        pack.RootPrefix is null && pack.FileNames.Contains(MultiMcHelper.PACK_INDEX_FILE_NAME);

    public async Task<ImportedProfileContainer> ExtractAsync(CompressedProfilePack pack)
    {
        await using var indexStream = pack.Open(MultiMcHelper.PACK_INDEX_FILE_NAME);
        var mmcPack = await JsonSerializer
                           .DeserializeAsync<MmcPack>(indexStream, JsonSerializerOptions.Web)
                           .ConfigureAwait(false);
        if (mmcPack is null)
        {
            throw new FormatException($"{MultiMcHelper.PACK_INDEX_FILE_NAME} is not a valid mmc-pack.json");
        }

        var mcVersion = mmcPack.Components.FirstOrDefault(c => c.Uid == MultiMcHelper.UID_MINECRAFT)?.Version;
        if (mcVersion is null)
        {
            throw new FormatException("mmc-pack.json does not contain net.minecraft component");
        }

        string? loaderLurl = null;
        foreach (var component in mmcPack.Components)
        {
            if (MultiMcHelper.UidToLoaderMappings.TryGetValue(component.Uid, out var loaderId))
            {
                loaderLurl = LoaderHelper.ToLurl(loaderId, component.Version);
                break;
            }
        }

        string? instanceName = null;
        if (pack.FileNames.Contains(MultiMcHelper.PACK_INSTANCE_CFG))
        {
            await using var cfgStream = pack.Open(MultiMcHelper.PACK_INSTANCE_CFG);
            using var reader = new StreamReader(cfgStream);
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
                {
                    instanceName = line["name=".Length..];
                    break;
                }
            }
        }

        var importFileNames = pack
                             .FileNames
                             .Where(x => x.StartsWith(MultiMcHelper.PACK_MINECRAFT_DIR)
                                      && x != MultiMcHelper.PACK_MINECRAFT_DIR
                                      && x.Length > MultiMcHelper.PACK_MINECRAFT_DIR.Length + 1)
                             .Select(x => (x, x[(MultiMcHelper.PACK_MINECRAFT_DIR.Length + 1)..]))
                             .Where(x => ZipArchiveHelper.IsExtractableEntry(x.Item2))
                             .ToList();
        var (launchFiles, generatedFiles, diagnostics) = await ConvertPatchesAsync(pack, mmcPack).ConfigureAwait(false);

        return new(new()
        {
            Name = instanceName ?? "Imported MultiMc Pack",
            Setup = new() { Version = mcVersion, Loader = loaderLurl, Packages = [] }
        },
                   importFileNames,
                   [("pack.png", "icon.png")],
                   null,
                   launchFiles,
                   generatedFiles,
                   diagnostics,
                   true);
    }

    #endregion

    private static async Task<(IReadOnlyList<(string Source, string Target)> LaunchFiles,
                               IReadOnlyList<(string Target, byte[] Content)> GeneratedFiles,
                               IReadOnlyList<LaunchPlanDiagnostic> Diagnostics)> ConvertPatchesAsync(
        CompressedProfilePack pack,
        MmcPack mmcPack)
    {
        var patchNames = pack.FileNames
                            .Where(x => x.StartsWith("patches/", StringComparison.Ordinal)
                                     && x.EndsWith(".json", StringComparison.Ordinal)
                                     && ZipArchiveHelper.IsExtractableEntry(x))
                            .ToHashSet(StringComparer.Ordinal);
        var componentOrder = mmcPack.Components.Select((component, index) => (component.Uid, index))
                                               .ToDictionary(x => x.Uid, x => x.index, StringComparer.Ordinal);
        var ordered = patchNames.OrderBy(x => componentOrder.TryGetValue(UidOf(x), out var index) ? index : int.MaxValue)
                                .ThenBy(x => x, StringComparer.Ordinal)
                                .ToArray();
        var launchFiles = new List<(string Source, string Target)>();
        var generatedFiles = new List<(string Target, byte[] Content)>();
        var diagnostics = new List<LaunchPlanDiagnostic>();
        var referencedFiles = new HashSet<string>(StringComparer.Ordinal);
        var usedUids = new HashSet<string>(StringComparer.Ordinal);
        var width = Math.Max(3, Math.Max(ordered.Length - 1, 0).ToString().Length);
        for (var index = 0; index < ordered.Length; index++)
        {
            var source = ordered[index];
            var uid = UidOf(source);
            if (!LibraryHelper.IsSafeIdentifier(uid))
            {
                throw new FormatException($"MMC patch '{source}' has an invalid UID");
            }

            var fileUid = LaunchPlanFileHelper.DisambiguateUid(uid, source, usedUids);
            await using var stream = pack.Open(source);
            var patch = await JsonSerializer.DeserializeAsync<MmcPatch>(stream, JsonSerializerOptions.Web)
                                 .ConfigureAwait(false);
            if (patch is null)
            {
                throw new FormatException($"MMC patch '{source}' is not valid JSON");
            }

            var converted = MultiMcPatchConverter.Convert(uid, patch);
            diagnostics.AddRange(converted.Diagnostics.Select(x => x with { Path = source }));
            var order = index.ToString($"D{width}");
            generatedFiles.Add(($"{LaunchPlanFileHelper.SOURCE_PREFIX}{order}-{fileUid}{LaunchPlanFileHelper.PLAN_SUFFIX}",
                                MultiMcPatchConverter.Serialize(converted.Document)));
            foreach (var relative in ReferencedFiles(patch))
            {
                if (!referencedFiles.Add(relative))
                {
                    continue;
                }

                var archivePath = pack.FileNames.Contains(relative)
                                       ? relative
                                       : $".minecraft/{relative}";
                if (!pack.FileNames.Contains(archivePath))
                {
                    diagnostics.Add(new(LaunchPlanDiagnostic.Kind.Warning,
                                        $"MMC patch '{uid}' references missing local file '{relative}'.",
                                        source));
                    continue;
                }

                launchFiles.Add((archivePath, $"{LaunchPlanFileHelper.SOURCE_PREFIX}{relative}"));
            }
        }

        return (launchFiles, generatedFiles, diagnostics);

        static string UidOf(string path) => path["patches/".Length..^".json".Length];

        static IEnumerable<string> ReferencedFiles(MmcPatch patch)
        {
            foreach (var library in (patch.Libraries ?? []).Concat(patch.AdditionalLibraries ?? []))
            {
                var url = library.Url ?? library.Downloads?.Artifact?.Url;
                if (url is { IsAbsoluteUri: false })
                {
                    yield return url.OriginalString.Replace('\\', '/');
                }
            }

            if (patch.AssetIndex?.Url is { IsAbsoluteUri: false } asset)
            {
                yield return asset.OriginalString.Replace('\\', '/');
            }
        }
    }
}
