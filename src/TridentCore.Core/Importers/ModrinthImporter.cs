using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Importers;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Models.ModrinthPack;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;

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

    private async Task<Profile.Rice.Entry> ToPackageAsync(PackIndex.IndexFile file, string? source)
    {
        // FIX: 需要兼容 bbsmc
        //  bbsmc 用的第三方包，其中部分使用 mrpack，而 mrpack 使用多个源，其中就有 forgecdn
        //  也就是 mrpack 可以包含多个托管站
        // FIX: 有些 %版本% 写的是文件名
        foreach (var download in file.Downloads)
        {
            var path = download.AbsolutePath;

            switch (download.Host)
            {
                // https://cdn.modrinth.com/data/88888888/versions/88888888/filename.jar
                case "cdn.modrinth.com" when path.Length > 32:
                {
                    var projectId = path[6..14];
                    var versionId = path[24..32];
                    return new()
                    {
                        Pref = PackageHelper.ToPref(ModrinthHelper.LABEL, null, projectId, versionId),
                        Enabled = true,
                        Source = source
                    };
                }
                // https://edge.forgecdn.net/files/1234/567/filename.jar
                case "edge.forgecdn.net":
                {
                    var segments = download.Segments;
                    // /files/1234/567/filename.jar => ["/", "files/", "1234/", "567/", "filename.jar"]
                    if (segments is [_, "files/", _, _, ..])
                    {
                        var part1 = segments[2].TrimEnd('/');
                        var part2 = segments[3].TrimEnd('/');
                        if (uint.TryParse(part1, out var high) && uint.TryParse(part2, out var low))
                        {
                            var fileId = high * 1000 + low;
                            var fileInfo = await repository.GetCurseForgeFileAsync(fileId).ConfigureAwait(false);
                            if (fileInfo is not null)
                            {
                                return new()
                                {
                                    Pref = PackageHelper.ToPref(CurseForgeHelper.LABEL,
                                                                null,
                                                                fileInfo.ModId.ToString(),
                                                                fileId.ToString()),
                                    Enabled = true,
                                    Source = source
                                };
                            }
                        }
                    }

                    break;
                }
            }
        }

        throw new NotSupportedException($"{file.Path} can not be recognized as an attachment");
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
        if (index is null
         || !TryExtractLoader(index.Dependencies, out var loader)
         || !TryExtractVersion(index.Dependencies, out var version))
        {
            throw new FormatException($"{ModrinthHelper.PACK_INDEX_FILE_NAME} is not a valid manifest");
        }

        var source = pack.Reference is not null ? PackageHelper.ToPref(pack.Reference) : null;
        var packageTasks = index
                          .Files.Where(x => x.Env?.Client is not "unsupported")
                          .Select(x => ToPackageAsync(x, source))
                          .ToArray();
        var packages = await Task.WhenAll(packageTasks).ConfigureAwait(false);

        return new(new()
                   {
                       Name = index.Name,
                       Setup =
                           new()
                           {
                               Source = source,
                               Version = version,
                               Loader = LoaderHelper.ToLurl(loader.Identity, loader.Version),
                               Packages = [.. packages]
                           }
                   },
        [
            .. pack
              .FileNames
              .Where(x => x.StartsWith("overrides") && x != "overrides" && x.Length > "overrides".Length + 1)
              .Select(x => (x, x[("overrides".Length + 1)..]))
              .Where(x => !x.Item2.EndsWith('/') && !ZipArchiveHelper.InvalidNames.Contains(x.Item2)),
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
