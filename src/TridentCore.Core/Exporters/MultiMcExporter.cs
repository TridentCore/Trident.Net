using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TridentCore.Abstractions.Exporters;
using TridentCore.Abstractions.Extensions;
using TridentCore.Abstractions.Repositories;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Engines.Deploying;
using TridentCore.Core.Models.MultiMcPack;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Exporters;

public class MultiMcExporter(
    PrismLauncherService prismLauncherService,
    IServiceProvider serviceProvider
) : IProfileExporter
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    #region IProfileExporter Members

    public string Label => MultiMcHelper.LABEL;

    public async Task<PackedProfileContainer> PackAsync(UncompressedProfilePack pack)
    {
        var container = new PackedProfileContainer(pack.Key)
        {
            OverrideDirectoryName = MultiMcHelper.PACK_MINECRAFT_DIR,
        };
        var setup = pack.Profile.Setup;

        // mmc-pack.json: resolve LWJGL version from Prism Launcher metadata
        var mcComponent = await prismLauncherService
            .GetVersionAsync(MultiMcHelper.UID_MINECRAFT, setup.Version, default)
            .ConfigureAwait(false);

        var lwjglVersion = mcComponent.Requires
            .FirstOrDefault(r => r.Uid == MultiMcHelper.UID_LWJGL3)
            ?.Suggest;

        var components = new List<MmcPack.ComponentEntry>
        {
            new(MultiMcHelper.UID_MINECRAFT, setup.Version),
        };

        if (lwjglVersion is not null)
        {
            components.Insert(0, new(MultiMcHelper.UID_LWJGL3, lwjglVersion));
        }

        if (!string.IsNullOrEmpty(setup.Loader) && LoaderHelper.TryParse(setup.Loader, out var loader))
        {
            if (MultiMcHelper.LoaderToUidMappings.TryGetValue(loader.Identity, out var uid))
            {
                components.Add(new(uid, loader.Version));
            }
        }

        var mmcPack = new MmcPack(1, components);
        var mmcPackStream = new MemoryStream();
        await JsonSerializer
            .SerializeAsync(mmcPackStream, mmcPack, SerializerOptions)
            .ConfigureAwait(false);
        mmcPackStream.Position = 0;
        container.Attachments.Add(MultiMcHelper.PACK_INDEX_FILE_NAME, mmcPackStream);

        // instance.cfg
        var instanceCfgBuilder = new StringBuilder();
        instanceCfgBuilder.AppendLine($"name={pack.Name}");
        instanceCfgBuilder.AppendLine("InstanceType=OneSix");
        var instanceCfgBytes = Encoding.UTF8.GetBytes(instanceCfgBuilder.ToString());
        var instanceCfgStream = new MemoryStream(instanceCfgBytes);
        instanceCfgStream.Position = 0;
        container.Attachments.Add(MultiMcHelper.PACK_INSTANCE_CFG, instanceCfgStream);

        // pack all mod files into .minecraft/ (MultiMC format is always offline)
        var planner = serviceProvider.GetRequiredService<PackagePlanner>();
        var materializer = serviceProvider.GetRequiredService<PackageMaterializer>();
        var plans = await planner
            .PlanAsync(
                setup.Packages.Where(x => x.Enabled).ToList(),
                new(
                    setup.Rules.Where(x => x.Enabled).ToList(),
                    Filter.FromSetup(setup)
                )
            )
            .ToListAsync()
            .ConfigureAwait(false);
        var bag = new ConcurrentBag<(string, string)>();
        await materializer
            .MaterializeAsync(
                plans,
                (plan, _, path) => { bag.Add((plan.RelativeTargetPath, path)); }
            )
            .ConfigureAwait(false);
        foreach (var (rel, abs) in bag)
        {
            var relative = Path.Combine(container.OverrideDirectoryName, rel);
            container.Files.Add(relative, abs);
        }

        return container;
    }

    #endregion
}
