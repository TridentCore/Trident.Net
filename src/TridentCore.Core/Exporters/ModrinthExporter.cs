using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using TridentCore.Abstractions.Exporters;
using TridentCore.Abstractions.Extensions;
using TridentCore.Abstractions.Repositories;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Engines.Deploying;
using TridentCore.Core.Models.ModrinthPack;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;
using TridentCore.Purl;

namespace TridentCore.Core.Exporters;

public class ModrinthExporter(RepositoryAgent agent, IServiceProvider serviceProvider) : IProfileExporter
{
    private static readonly Dictionary<string, string> LoaderMappings = new()
    {
        [LoaderHelper.LOADERID_FORGE] = "forge",
        [LoaderHelper.LOADERID_NEOFORGE] = "neoforge",
        [LoaderHelper.LOADERID_FABRIC] = "fabric-loader",
        [LoaderHelper.LOADERID_QUILT] = "quilt-loader",
    };

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    #region IProfileExporter Members

    public string Label => ModrinthHelper.LABEL;

    public async Task<PackedProfileContainer> PackAsync(UncompressedProfilePack pack)
    {
        var container = new PackedProfileContainer(pack.Key) { OverrideDirectoryName = "overrides", };
        var setup = pack.Profile.Setup;

        (string Identity, string Version)? loader = !string.IsNullOrEmpty(setup.Loader)
                                                 && LoaderHelper.TryParse(setup.Loader, out var result)
                                                        ? result
                                                        : null;

        var files = new List<PackIndex.IndexFile>();
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
            var packages = setup
                          .Packages.Where(x => x.Enabled)
                          .Select(x => PackageHelper.TryParse(x.Purl, out var pkg)
                                           ? new PackageIdentifier(pkg.Label, pkg.Namespace, pkg.Pid, pkg.Vid)
                                           : throw new FormatException($"Package {x.Purl} is not a valid package"))
                          .ToList();
            var resolved = await agent
                                .ResolveBatchAsync(packages, new(pack.Profile.Setup.Version, loader?.Identity, null))
                                .ConfigureAwait(false);

            files.AddRange(resolved
                          .Select(x => x.Item2)
                          .Select(package =>
                                      new
                                          PackIndex.IndexFile($"{FileHelper.GetAssetFolderName(package.Kind)}/{package.FileName}",
                                                              new(package.Sha1, null),
                                                              new("required", "unsupported"),
                                                              [package.Download],
                                                              package.Size)));
        }

        var dependencies = new Dictionary<string, string> { { "minecraft", setup.Version } };
        if (loader is not null && LoaderMappings.TryGetValue(loader.Value.Identity, out var mapping))
        {
            dependencies.Add(mapping, loader.Value.Version);
        }

        var index = new PackIndex(1, "minecraft", pack.Version, pack.Name, null, files, dependencies);
        var indexStream = new MemoryStream();
        await JsonSerializer.SerializeAsync(indexStream, index, SerializerOptions).ConfigureAwait(false);
        indexStream.Position = 0;
        container.Attachments.Add(ModrinthHelper.PACK_INDEX_FILE_NAME, indexStream);
        return container;
    }

    #endregion
}
