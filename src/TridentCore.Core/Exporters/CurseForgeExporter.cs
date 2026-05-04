using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TridentCore.Abstractions.Exporters;
using TridentCore.Abstractions.Extensions;
using TridentCore.Abstractions.Repositories;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Engines.Deploying;
using TridentCore.Core.Models.CurseForgePack;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Exporters;

public class CurseForgeExporter(IServiceProvider serviceProvider) : IProfileExporter
{
    private static readonly Dictionary<string, string> LoaderMappings = new()
    {
        [LoaderHelper.LOADERID_FORGE] = "forge",
        [LoaderHelper.LOADERID_NEOFORGE] = "neoforge",
        [LoaderHelper.LOADERID_FABRIC] = "fabric",
        [LoaderHelper.LOADERID_QUILT] = "quilt",
    };

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true, };

    #region IProfileExporter Members

    public string Label => CurseForgeHelper.LABEL;

    public async Task<PackedProfileContainer> PackAsync(UncompressedProfilePack pack)
    {
        var container = new PackedProfileContainer(pack.Key) { OverrideDirectoryName = "overrides", };
        var setup = pack.Profile.Setup;
        var attachments = new List<Manifest.FileModel>();

        if (pack.Options.OfflineMode)
        {
            var planner = serviceProvider.GetRequiredService<PackagePlanner>();
            var materializer = serviceProvider.GetRequiredService<PackageMaterializer>();
            var plans = await planner
                             .PlanAsync(setup.Packages.Where(x => x.Enabled).ToList(),
                                        new(setup.Rules.Where(x => x.Enabled).ToList(), Filter.FromSetup(setup)))
                             .ToListAsync()
                             .ConfigureAwait(false);
            var bag = new ConcurrentBag<(string, string)>();
            await materializer
                 .MaterializeAsync(plans,
                                   (plan, _, path) =>
                                   {
                                       bag.Add((plan.RelativeTargetPath, path));
                                   })
                 .ConfigureAwait(false);
            foreach (var (rel, abs) in bag)
            {
                var relative = Path.Combine(container.OverrideDirectoryName, rel);
                container.Files.Add(relative, abs);
            }
        }
        else
        {
            foreach (var entry in setup.Packages.Where(x => x.Enabled))
            {
                if (PackageHelper.TryParse(entry.Purl, out var parsed))
                {
                    if (uint.TryParse(parsed.Pid, out var pid) && uint.TryParse(parsed.Vid, out var vid))
                    {
                        attachments.Add(new(pid, vid, true));
                    }
                }
                else
                {
                    throw new NotSupportedException("CurseForge exporter only supports CurseForge packages");
                }
            }
        }

        var manifest = new Manifest(new(setup.Version, MakeLoader(setup.Loader)),
                                    "minecraftModpack",
                                    1,
                                    pack.Name,
                                    pack.Version,
                                    pack.Author,
                                    attachments,
                                    "overrides");
        var manifestStream = new MemoryStream();
        await JsonSerializer.SerializeAsync(manifestStream, manifest, SerializerOptions).ConfigureAwait(false);
        manifestStream.Position = 0;
        container.Attachments.Add(CurseForgeHelper.PACK_INDEX_FILE_NAME, manifestStream);
        return container;
    }

    #endregion

    private IReadOnlyList<Manifest.MinecraftModel.ModLoaderModel> MakeLoader(string? lurl)
    {
        if (!string.IsNullOrEmpty(lurl)
         && LoaderHelper.TryParse(lurl, out var tuple)
         && LoaderMappings.TryGetValue(tuple.Identity, out var mapping))
        {
            return [new($"{mapping}-{tuple.Version}", true)];
        }

        return [];
    }
}
