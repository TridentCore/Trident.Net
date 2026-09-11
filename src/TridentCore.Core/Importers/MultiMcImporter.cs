using System.Text.Json;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Importers;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Models.MultiMcPack;
using TridentCore.Core.Models.PrismLauncherApi;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Importers;

public class MultiMcImporter : IProfileImporter
{
    #region IProfileImporter Members

    public bool CanHandle(CompressedProfilePack pack) =>
        pack.FileNames.Contains(MultiMcHelper.PACK_INDEX_FILE_NAME);

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

        // NOTE: 只读归档自带的组件定义（用户「从文件安装」组件时才会落盘）。其余组件的启动声明留给
        //  标准产出阶段按 profile 的版本与加载器获取，导入期不联网，因此离线归档始终可导入。
        var definitions = await MultiMcPatchHelper.ReadDefinitionsAsync(pack, mmcPack).ConfigureAwait(false);

        var minecraft = mmcPack.Components.FirstOrDefault(c => c.Uid == MultiMcHelper.UID_MINECRAFT && !c.Disabled);
        var mcVersion = minecraft?.Version
                     ?? minecraft?.CachedVersion
                     ?? definitions.GetValueOrDefault(MultiMcHelper.UID_MINECRAFT)?.Version;
        if (string.IsNullOrWhiteSpace(mcVersion))
        {
            throw new FormatException("mmc-pack.json does not contain net.minecraft component");
        }

        // NOTE: 组件声明的 uid 即加载器身份，声明了什么就用什么——不去猜、不去纠正。
        string? loaderLurl = null;
        foreach (var component in mmcPack.Components.Where(x => !x.Disabled))
        {
            if (!MultiMcHelper.UidToLoaderMappings.TryGetValue(component.Uid, out var loaderId))
            {
                continue;
            }

            var version = component.Version
                       ?? component.CachedVersion
                       ?? definitions.GetValueOrDefault(component.Uid)?.Version;
            if (!string.IsNullOrWhiteSpace(version))
            {
                loaderLurl = LoaderHelper.ToLurl(loaderId, version);
            }

            break;
        }

        string? instanceName = null;
        string? instanceArguments = null;
        // NOTE: OverrideJavaArgs 缺省为 false（Prism 的默认值），此时 JvmArgs 只是全局参数的一份
        //  未被启用的覆盖，不能当作实例参数套用。
        var overrideArguments = false;
        if (pack.FileNames.Contains(MultiMcHelper.PACK_INSTANCE_CFG))
        {
            await using var cfgStream = pack.Open(MultiMcHelper.PACK_INSTANCE_CFG);
            using var reader = new StreamReader(cfgStream);
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
                {
                    instanceName = line["name=".Length..];
                }
                else if (line.StartsWith("JvmArgs=", StringComparison.OrdinalIgnoreCase))
                {
                    instanceArguments = line["JvmArgs=".Length..];
                }
                else if (line.StartsWith("OverrideJavaArgs=", StringComparison.OrdinalIgnoreCase))
                {
                    overrideArguments = string.Equals(line["OverrideJavaArgs=".Length..], "true", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        if (!overrideArguments)
        {
            instanceArguments = null;
        }

        MultiMcPatchHelper.Result? patches = null;
        if (definitions.Count > 0 || !string.IsNullOrWhiteSpace(instanceArguments))
        {
            patches = MultiMcPatchHelper.Convert(pack, mmcPack, definitions, instanceArguments);
        }

        var importFileNames = pack
                             .FileNames
                             .Where(x => x.StartsWith(MultiMcHelper.PACK_MINECRAFT_DIR)
                                      && x != MultiMcHelper.PACK_MINECRAFT_DIR
                                      && x.Length > MultiMcHelper.PACK_MINECRAFT_DIR.Length + 1)
                             .Select(x => (x, x[(MultiMcHelper.PACK_MINECRAFT_DIR.Length + 1)..]))
                             .Where(x => ZipArchiveHelper.IsExtractableEntry(x.Item2))
                             .ToList();

        return new(new()
        {
            Name = instanceName ?? "Imported MultiMc Pack",
            Setup = new() { Version = mcVersion, Loader = loaderLurl, Packages = [] }
        },
                   importFileNames,
                   [("pack.png", "icon.png")],
                   null)
        {
            Patches = new() { Files = patches?.Files ?? [], Generated = patches?.Generated ?? new Dictionary<string, byte[]>() }
        };
    }

    #endregion
}
