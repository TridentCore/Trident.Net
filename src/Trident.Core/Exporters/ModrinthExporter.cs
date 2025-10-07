using System.Text.Json;
using Trident.Abstractions.Exporters;
using Trident.Abstractions.Utilities;
using Trident.Core.Models.ModrinthPack;
using Trident.Core.Services;
using Trident.Core.Utilities;

namespace Trident.Core.Exporters;

public class ModrinthExporter(RepositoryAgent agent) : IProfileExporter
{
    private static readonly Dictionary<string, string> LoaderMappings = new()
    {
        [LoaderHelper.LOADERID_FORGE] = "forge",
        [LoaderHelper.LOADERID_NEOFORGE] = "neoforge",
        [LoaderHelper.LOADERID_FABRIC] = "fabric-loader",
        [LoaderHelper.LOADERID_QUILT] = "quilt-loader"
    };

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    #region IProfileExporter Members

    public string Label => ModrinthHelper.LABEL;

    public async Task<PackedProfileContainer> PackAsync(UncompressedProfilePack pack)
    {
        var container = new PackedProfileContainer(pack.Key) { OverrideDirectoryName = "overrides" };
        var setup = pack.Profile.Setup;

        var packages = setup
                      .Packages.Where(x => x.Enabled)
                      .Select(x => PackageHelper.TryParse(x.Purl, out var pkg)
                                       ? pkg
                                       : throw new FormatException($"Package {x.Purl} is not a valid package"))
                      .ToList();

        (string Identity, string Version)? loader = !string.IsNullOrEmpty(setup.Loader)
                                                 && LoaderHelper.TryParse(setup.Loader, out var result)
                                                        ? result
                                                        : null;

        var resolved = await agent
                            .ResolveBatchAsync(packages, new(pack.Profile.Setup.Version, loader?.Identity, null))
                            .ConfigureAwait(false);

        var files = resolved
                   .Select(package =>
                               new
                                   PackIndex.IndexFile($"{FileHelper.GetAssetFolderName(package.Kind)}/{package.FileName}",
                                                       new(package.Sha1, null),
                                                       new("required", "unsupported"),
                                                       [package.Download],
                                                       package.Size))
                   .ToList();

        var dependencies = new Dictionary<string, string>();
        dependencies.Add("minecraft", setup.Version);
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
