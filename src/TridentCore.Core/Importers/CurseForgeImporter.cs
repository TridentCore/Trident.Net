using System.Text.Json;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Importers;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Models.CurseForgePack;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Importers;

public class CurseForgeImporter : IProfileImporter
{
    private static readonly Dictionary<string, string> LOADER_MAPPINGS = new()
    {
        ["forge"] = LoaderHelper.LOADERID_FORGE,
        ["neoforge"] = LoaderHelper.LOADERID_NEOFORGE,
        ["fabric"] = LoaderHelper.LOADERID_FABRIC,
        ["quilt"] = LoaderHelper.LOADERID_QUILT,
    };

    #region IProfileImporter Members

    public bool CanHandle(CompressedProfilePack pack) =>
        pack.RootPrefix is null && pack.FileNames.Contains(CurseForgeHelper.PACK_INDEX_FILE_NAME);

    public async Task<ImportedProfileContainer> ExtractAsync(CompressedProfilePack pack)
    {
        await using var manifestStream = pack.Open(CurseForgeHelper.PACK_INDEX_FILE_NAME);
        var manifest = await JsonSerializer
            .DeserializeAsync<Manifest>(manifestStream, JsonSerializerOptions.Web)
            .ConfigureAwait(false);
        if (manifest is null || !TryExtractLoader(manifest.Minecraft.ModLoaders, out var loader))
        {
            throw new FormatException($"{CurseForgeHelper.PACK_INDEX_FILE_NAME} is not a valid manifest");
        }

        var source = pack.Reference is not null ? PackageHelper.ToPref(pack.Reference) : null;
        return new(
            new()
            {
                Name = manifest.Name,
                Setup = new()
                {
                    Version = manifest.Minecraft.Version,
                    Source = source,
                    Loader = LoaderHelper.ToLurl(loader.Identity, loader.Version),
                    Packages =
                    [
                        .. manifest.Files.Select(x => new Profile.Rice.Entry
                        {
                            Enabled = x.Required,
                            Pref = PackageHelper.ToPref(
                                CurseForgeHelper.LABEL,
                                null,
                                x.ProjectID.ToString(),
                                x.FileID.ToString()
                            ),
                            Source = source,
                        }),
                    ],
                },
            },
            pack.FileNames.Where(x =>
                    x.StartsWith(manifest.Overrides)
                    && x != manifest.Overrides
                    && x.Length > manifest.Overrides.Length + 1
                )
                .Select(x => (x, x[(manifest.Overrides.Length + 1)..]))
                .Where(x =>
                    !x.Item2.EndsWith('/')
                    && !x.Item2.EndsWith('\\')
                    && !ZipArchiveHelper.InvalidNames.Contains(x.Item2)
                )
                .ToList(),
            [],
            pack.Reference?.Thumbnail
        );
    }

    #endregion

    private static bool TryExtractLoader(
        IEnumerable<Manifest.MinecraftModel.ModLoaderModel> loaders,
        out (string Identity, string Version) loader
    )
    {
        var primary = loaders.FirstOrDefault(x => x.Primary);
        loader = default;
        if (primary is null || !primary.Id.Contains('-'))
        {
            return false;
        }

        var name = primary.Id[..primary.Id.IndexOf('-')];
        var ver = primary.Id[(primary.Id.IndexOf('-') + 1)..];
        if (LOADER_MAPPINGS.TryGetValue(name, out var mapping))
        {
            name = mapping;
        }

        loader = (name, ver);
        return true;
    }
}
