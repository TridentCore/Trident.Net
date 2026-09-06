using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TridentCore.Abstractions.Exporters;
using TridentCore.Abstractions.Extensions;
using TridentCore.Abstractions.Launching;
using TridentCore.Abstractions.Repositories;
using TridentCore.Core.Engines.Deploying;
using TridentCore.Core.Models.MultiMcPack;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Exporters;

public class MultiMcExporter(LaunchDefinitionResolverService resolver, IServiceProvider serviceProvider) : IProfileExporter
{
    public string Label => MultiMcHelper.LABEL;
    public bool SupportsLaunchDefinitions => true;

    public async Task<PackedProfileContainer> PackAsync(UncompressedProfilePack pack)
    {
        var container = new PackedProfileContainer(pack.Key) { OverrideDirectoryName = MultiMcHelper.PACK_MINECRAFT_DIR };
        try
        {
            var setup = pack.Profile.Setup;
            var snapshot = LaunchDefinitionSnapshot.Load(pack.Key, includeUser: false);
            var resolution = await resolver.ResolveAsync(setup, snapshot, CancellationToken.None).ConfigureAwait(false);
            var components = new List<MmcPack.ComponentEntry>();
            var gameArguments = new List<LaunchArgument>();
            foreach (var entry in resolution.Components)
            {
                var component = entry.Definition;
                components.Add(new(component.Id, component.Version));
                if (component.GameArguments.Replace is { } replacement) gameArguments = [.. replacement];
                gameArguments.AddRange(component.GameArguments.Append);
                if (!snapshot.Components.ContainsKey(component.Id)) continue;
                var patch = MetadataExportHelper.Convert(component, gameArguments, container);
                container.Attachments.Add($"patches/{component.Id}.json",
                    new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(patch, FileHelper.SerializerOptions)));
            }

            var selected = snapshot.Definition?.Components ?? [];
            for (var i = selected.Count - 1; i >= 0; i--)
            {
                if (selected[i].Enabled || LaunchDefinitionResolverService.Bind(selected[i], setup) is not { } bound) continue;
                var nextId = selected.Skip(i + 1).Select(x => LaunchDefinitionResolverService.Bind(x, setup)?.Id)
                    .FirstOrDefault(id => components.Any(x => x.Uid == id));
                var position = nextId is null ? components.Count : components.FindIndex(x => x.Uid == nextId);
                components.Insert(position, new(bound.Id, bound.Version, true));
                if (snapshot.Components.TryGetValue(bound.Id, out var local))
                {
                    var component = snapshot.Resolve(local).Definition;
                    var patch = MetadataExportHelper.Convert(component,
                        [.. component.GameArguments.Replace ?? [], .. component.GameArguments.Append], container);
                    container.Attachments.Add($"patches/{component.Id}.json",
                        new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(patch, FileHelper.SerializerOptions)));
                }
            }
            container.Attachments.Add(MultiMcHelper.PACK_INDEX_FILE_NAME,
                new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new MmcPack(1, components), FileHelper.SerializerOptions)));
            container.Attachments.Add(MultiMcHelper.PACK_INSTANCE_CFG,
                new MemoryStream(Encoding.UTF8.GetBytes($"name={pack.Name}{Environment.NewLine}InstanceType=OneSix{Environment.NewLine}")));

            var planner = serviceProvider.GetRequiredService<PackagePlanner>();
            var materializer = serviceProvider.GetRequiredService<PackageMaterializer>();
            var plans = await planner.PlanAsync([.. setup.Packages.Where(x => x.Enabled)],
                new([.. setup.Rules.Where(x => x.Enabled)], Filter.FromSetup(setup))).ToListAsync().ConfigureAwait(false);
            var files = new ConcurrentBag<(string Relative, string Absolute)>();
            await materializer.MaterializeAsync(plans, (plan, _, path) => files.Add((plan.RelativeTargetPath, path))).ConfigureAwait(false);
            foreach (var (relative, absolute) in files)
            {
                container.Files.Add(Path.Combine(container.OverrideDirectoryName, relative), absolute);
            }
            return container;
        }
        catch
        {
            container.Dispose();
            throw;
        }
    }
}
