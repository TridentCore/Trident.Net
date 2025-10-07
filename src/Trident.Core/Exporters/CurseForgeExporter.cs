using System.Text.Json;
using Trident.Abstractions.Exporters;
using Trident.Abstractions.Utilities;
using Trident.Core.Models.CurseForgePack;
using Trident.Core.Utilities;

namespace Trident.Core.Exporters;

public class CurseForgeExporter : IProfileExporter
{
    private static readonly Dictionary<string, string> LoaderMappings = new()
    {
        [LoaderHelper.LOADERID_FORGE] = "forge",
        [LoaderHelper.LOADERID_NEOFORGE] = "neoforge",
        [LoaderHelper.LOADERID_FABRIC] = "fabric",
        [LoaderHelper.LOADERID_QUILT] = "quilt"
    };

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    #region IProfileExporter Members

    public string Label => CurseForgeHelper.LABEL;

    public async Task<PackedProfileContainer> PackAsync(UncompressedProfilePack pack)
    {
        var container = new PackedProfileContainer(pack.Key) { OverrideDirectoryName = "overrides" };
        var setup = pack.Profile.Setup;
        var files = new List<Manifest.FileModel>();
        foreach (var entry in setup.Packages.Where(x => x.Enabled))
        {
            if (PackageHelper.TryParse(entry.Purl, out var parsed))
            {
                if (uint.TryParse(parsed.Pid, out var pid) && uint.TryParse(parsed.Vid, out var vid))
                {
                    files.Add(new(pid, vid, true));
                }
            }
            else
            {
                throw new NotSupportedException("CurseForge exporter only supports CurseForge packages");
            }
        }

        var manifest = new Manifest(new(setup.Version, MakeLoader(setup.Loader)),
                                    "minecraftModpack",
                                    1,
                                    pack.Name,
                                    pack.Version,
                                    pack.Author,
                                    files,
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
