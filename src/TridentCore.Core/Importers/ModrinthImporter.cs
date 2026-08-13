using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Importers;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Models.ModrinthPack;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;
using TridentCore.Pref;

namespace TridentCore.Core.Importers;

public class ModrinthImporter(RepositoryAgent repository) : IProfileImporter
{
    private static readonly Dictionary<string, string> LOADER_MAPPINGS = new()
    {
        ["forge"] = LoaderHelper.LOADERID_FORGE,
        ["neoforge"] = LoaderHelper.LOADERID_NEOFORGE,
        ["fabric-loader"] = LoaderHelper.LOADERID_FABRIC,
        ["quilt-loader"] = LoaderHelper.LOADERID_QUILT
    };

    private bool TryExtractLoader(
        IDictionary<string, string> dependencies,
        out (string Identity, string Version) loader)
    {
        foreach (var (k, v) in dependencies)
        {
            if (LOADER_MAPPINGS.TryGetValue(k, out var mapping))
            {
                loader = (mapping, v);
                return true;
            }
        }

        loader = default((string, string));
        return false;
    }

    private bool TryExtractVersion(IDictionary<string, string> dependencies, [MaybeNullWhen(false)] out string version)
    {
        if (dependencies.TryGetValue("minecraft", out var v))
        {
            version = v;
            return true;
        }

        version = null;
        return false;
    }

    #region IProfileImporter Members

    public bool CanHandle(CompressedProfilePack pack) =>
        pack.RootPrefix is null && pack.FileNames.Contains(ModrinthHelper.PACK_INDEX_FILE_NAME);

    public async Task<ImportedProfileContainer> ExtractAsync(CompressedProfilePack pack)
    {
        await using var manifestStream = pack.Open(ModrinthHelper.PACK_INDEX_FILE_NAME);
        var index = await JsonSerializer
                         .DeserializeAsync<PackIndex>(manifestStream, JsonSerializerOptions.Web)
                         .ConfigureAwait(false);
        if (index is null || !TryExtractVersion(index.Dependencies, out var version))
        {
            throw new FormatException($"{ModrinthHelper.PACK_INDEX_FILE_NAME} is not a valid manifest");
        }

        // NOTE: loader 可选——整合包可不声明（vanilla），符合 Modrinth 格式。
        var loader = TryExtractLoader(index.Dependencies, out var loaderInfo)
                         ? LoaderHelper.ToLurl(loaderInfo.Identity, loaderInfo.Version)
                         : null;

        var source = pack.Reference is not null ? PackageHelper.ToPref(pack.Reference) : null;

        // NOTE: 把所有文件的 downloads 展平成一次识别批，仓库层得以把同宿主 URL 折叠进
        //  原生批端点（如整包 forgecdn 链接一次 GetFilesAsync）。每文件取仓库识别的首个
        //  下载——保留作者源序。
        var files = index.Files.Where(x => x.Env?.Client is not "unsupported").ToArray();
        var downloads = files.SelectMany(x => x.Downloads).Distinct().ToArray();
        var recognized = await repository.RecognizeBatchAsync(downloads).ConfigureAwait(false);

        var packages = new List<Profile.Rice.Entry>();
        foreach (var file in files)
        {
            PackageIdentifier? match = null;
            foreach (var download in file.Downloads)
            {
                if (recognized.Successful.TryGetValue(download, out var id))
                {
                    match = id;
                    break;
                }
            }

            if (match is null)
            {
                // NOTE: the batch result is total — every download is in Successful or Failed — so when
                //  nothing matched, Failed holds the underlying cause (e.g. a repository rate-limit);
                //  surface it instead of a generic "unrecognized".
                foreach (var download in file.Downloads)
                {
                    if (recognized.Failed.TryGetValue(download, out var error))
                    {
                        throw error;
                    }
                }

                throw new NotSupportedException($"{file.Path} can not be recognized as an attachment");
            }

            packages.Add(new() { Pref = PackageHelper.ToPref(match.Value), Enabled = true, Source = source });
        }

        return new(new()
        {
            Name = index.Name,
            Setup =
                           new() { Source = source, Version = version, Loader = loader, Packages = [.. packages] }
        },
                   [
                       .. pack
                         .FileNames
                         .Where(x => x.StartsWith("overrides") && x != "overrides" && x.Length > "overrides".Length + 1)
                         .Select(x => (x, x[("overrides".Length + 1)..]))
                         .Where(x => !x.Item2.EndsWith('/')
                                  && !x.Item2.EndsWith('\\')
                                  && !ZipArchiveHelper.InvalidNames.Contains(x.Item2)),
                       .. pack
                         .FileNames
                         .Where(x => x.StartsWith("client-overrides")
                                  && x != "client-overrides"
                                  && x.Length > "client-overrides".Length + 1)
                         .Select(x => (x, x[("client-overrides".Length + 1)..]))
                         .Where(x => !x.Item2.EndsWith('/')
                                  && !x.Item2.EndsWith('\\')
                                  && !ZipArchiveHelper.InvalidNames.Contains(x.Item2))
                   ],
                   [],
                   pack.Reference?.Thumbnail);
    }

    #endregion
}
