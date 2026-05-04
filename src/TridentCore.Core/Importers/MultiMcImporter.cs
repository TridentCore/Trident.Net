using System.Text.Json;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Importers;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Models.MultiMcPack;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Importers;

public class MultiMcImporter : IProfileImporter
{
    #region IProfileImporter Members

    public string IndexFileName => MultiMcHelper.PACK_INDEX_FILE_NAME;

    public async Task<ImportedProfileContainer> ExtractAsync(CompressedProfilePack pack)
    {
        await using var indexStream = pack.Open(IndexFileName);
        var mmcPack = await JsonSerializer
            .DeserializeAsync<MmcPack>(indexStream, JsonSerializerOptions.Web)
            .ConfigureAwait(false);
        if (mmcPack is null)
        {
            throw new FormatException($"{IndexFileName} is not a valid mmc-pack.json");
        }

        var mcVersion = mmcPack.Components
            .FirstOrDefault(c => c.Uid == MultiMcHelper.UID_MINECRAFT)
            ?.Version;
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

        var importFileNames = pack.FileNames
            .Where(x =>
                x.StartsWith(MultiMcHelper.PACK_MINECRAFT_DIR)
                && x != MultiMcHelper.PACK_MINECRAFT_DIR
                && x.Length > MultiMcHelper.PACK_MINECRAFT_DIR.Length + 1
            )
            .Select(x => (x, x[(MultiMcHelper.PACK_MINECRAFT_DIR.Length + 1)..]))
            .Where(x =>
                !x.Item2.EndsWith('/')
                && !x.Item2.EndsWith('\\')
                && !ZipArchiveHelper.InvalidNames.Contains(x.Item2)
            )
            .ToList();

        return new(
            new()
            {
                Name = instanceName ?? "Imported MultiMc Pack",
                Setup = new()
                {
                    Version = mcVersion,
                    Loader = loaderLurl,
                    Packages = [],
                },
            },
            importFileNames,
            [],
            null
        );
    }

    #endregion
}
