using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Trident.Abstractions;

namespace Trident.Core.Engines.Deploying.Stages;

public class GenerateManifestStage(IHttpClientFactory factory) : StageBase
{
    protected override async Task OnProcessAsync(CancellationToken token)
    {
        var manifest = new EntityManifest();

        var artifact = Context.Artifact!;

        var indexPath = PathDef.Default.FileOfAssetIndex(artifact.AssetIndex.Id);
        manifest.PresentFiles.Add(new(indexPath, artifact.AssetIndex.Url, artifact.AssetIndex.Sha1));
        var index = await GetAssetIndexAsync(indexPath, artifact.AssetIndex.Url, artifact.AssetIndex.Sha1)
                       .ConfigureAwait(false)
                 ?? throw new
                        InvalidOperationException("Asset index file is broken or not matched with builtin models");
        foreach (var obj in index.Objects)
        {
            var path = PathDef.Default.FileOfAssetObject(obj.Value.Hash);
            manifest.PresentFiles.Add(new(path,
                                          new($"https://resources.download.minecraft.net/{obj.Value.Hash[..2]}/{obj.Value.Hash}",
                                              UriKind.Absolute),
                                          obj.Value.Hash));
        }

        foreach (var parcel in artifact.Parcels)
        {
            manifest.FragileFiles.Add(new(PathDef.Default.FileOfPackageObject(parcel.Label,
                                                                              parcel.Namespace,
                                                                              parcel.Pid,
                                                                              parcel.Vid,
                                                                              Path.GetExtension(parcel.Path)),
                                          Path.Combine(PathDef.Default.DirectoryOfHome(Context.Key), parcel.Path),
                                          parcel.Download,
                                          parcel.Sha1));
        }

        var nativesDir = PathDef.Default.DirectoryOfNatives(Context.Key);
        foreach (var lib in artifact.Libraries)
        {
            var path = PathDef.Default.FileOfLibrary(lib.Id.Namespace,
                                                     lib.Id.Name,
                                                     lib.Id.Version,
                                                     lib.Id.Platform,
                                                     lib.Id.Extension);
            manifest.PresentFiles.Add(new(path, lib.Url, lib.Sha1));
            if (lib.IsNative)
            {
                manifest.ExplosiveFiles.Add(new(path, nativesDir));
            }
        }

        if (Context.Runtime != null)
        {
            var path = PathDef.Default.FileOfRuntimeBundle(Context.Runtime.Major);
            var dir = PathDef.Default.DirectoryOfRuntime(Context.Runtime.Major);
            manifest.PresentFiles.Add(new(path, Context.Runtime.Url, null));
            manifest.ExplosiveFiles.Add(new(path, dir, true));
        }

        var buildDir = PathDef.Default.DirectoryOfBuild(Context.Key);
        var importDir = PathDef.Default.DirectoryOfImport(Context.Key);
        var liveDir = PathDef.Default.DirectoryOfLive(Context.Key);
        var persistDir = PathDef.Default.DirectoryOfPersist(Context.Key);
        // 将 import -> live 查漏补缺，会因为用户手动文件操作而产生 live 的文件比 import 多的情况
        // 但这视为用户行为，在完全的程序托管下只有 RESET/UPDATE 两种情况会修改 import/live，PROJECT 一种情况会修改 live，不会有文件多出来的意外而导致脱离控制的情况
        // 按优先级 live/import/persist ，后面的会把前面的顶掉
        // FIX: 如果 live/screenshots/.keep 且 persist/screenshots/a.png 则会把 a.png 塞进 live/screenshots/a.png，但好像无伤大雅
        var set = new Dictionary<string, EntityManifest.PersistentFile>();
        PopulatePersistent(set, importDir, importDir, liveDir, false);
        PopulatePersistent(set, importDir, liveDir, buildDir, true);
        PopulatePersistent(set, persistDir, persistDir, buildDir, true);
        foreach (var file in set.Values)
        {
            manifest.PersistentFiles.Add(file);
        }

        Context.Manifest = manifest;
    }

    private static void PopulatePersistent(
        IDictionary<string, EntityManifest.PersistentFile> collection,
        string scanDir,
        string sourceDir,
        string targetDir,
        bool phantom)
    {
        if (Directory.Exists(scanDir))
        {
            var dirs = new Stack<string>();
            dirs.Push(scanDir);

            while (dirs.TryPop(out var sub))
            {
                if (File.Exists(Path.Combine(sub, ".keep")))
                {
                    var dirRel = Path.GetRelativePath(scanDir, sub);
                    var dirTar = Path.Combine(targetDir, dirRel);
                    if (!collection.ContainsKey(dirRel))
                    {
                        collection[dirTar] = new(Path.Combine(sourceDir, dirRel), dirTar, phantom, true);
                    }
                }
                else
                {
                    foreach (var file in Directory.GetFiles(sub))
                    {
                        var fileRel = Path.GetRelativePath(scanDir, file);
                        var fileTar = Path.Combine(targetDir, fileRel);
                        collection[fileTar] = new(Path.Combine(sourceDir, fileRel), fileTar, phantom, false);
                    }

                    foreach (var dir in Directory.GetDirectories(sub))
                    {
                        dirs.Push(dir);
                    }
                }
            }
        }
    }

    private async ValueTask<MinecraftAssetIndex?> GetAssetIndexAsync(string indexFile, Uri url, string hash)
    {
        if (File.Exists(indexFile))
        {
            await using var reader = File.OpenRead(indexFile);
            var computed = Convert.ToHexStringLower(await SHA1.HashDataAsync(reader).ConfigureAwait(false));
            reader.Position = 0;
            if (computed == hash)
            {
                return await JsonSerializer
                            .DeserializeAsync<MinecraftAssetIndex>(reader, JsonSerializerOptions.Web)
                            .ConfigureAwait(false);
            }
        }

        using var client = factory.CreateClient();
        return await client.GetFromJsonAsync<MinecraftAssetIndex>(url, JsonSerializerOptions.Web).ConfigureAwait(false);
    }

    #region Nested type: MinecraftAssetIndex

    private record MinecraftAssetIndex(IDictionary<string, MinecraftAssetIndex.MinecraftAssetIndexObject> Objects)
    {
        #region Nested type: MinecraftAssetIndexObject

        public record MinecraftAssetIndexObject(string Hash, uint Size);

        #endregion
    }

    #endregion
}
