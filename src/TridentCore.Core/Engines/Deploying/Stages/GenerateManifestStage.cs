using System.Net.Http.Json;
using System.Text.Json;
using TridentCore.Abstractions;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Extensions;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;
using FileHash = TridentCore.Abstractions.Utilities.FileHash;

namespace TridentCore.Core.Engines.Deploying.Stages;

public class GenerateManifestStage(IHttpClientFactory factory) : StageBase
{
    protected override async Task OnProcessAsync(CancellationToken token)
    {
        var manifest = new EntityManifest();

        var artifact = Context.Lock.Artifact!;

        var indexPath = PathDef.Default.FileOfAssetIndex(artifact.AssetIndex.Id);
        manifest.PresentFiles.Add(new(indexPath, artifact.AssetIndex.Url, artifact.AssetIndex.Hash));
        var index = await GetAssetIndexAsync(indexPath, artifact.AssetIndex.Url, artifact.AssetIndex.Hash)
                       .ConfigureAwait(false)
                 ?? throw new
                        InvalidOperationException("Asset index file is broken or not matched with builtin models");
        foreach (var obj in index.Objects)
        {
            var path = PathDef.Default.FileOfAssetObject(obj.Value.Hash);
            manifest.PresentFiles.Add(new(path,
                                          new($"https://resources.download.minecraft.net/{obj.Value.Hash[..2]}/{obj.Value.Hash}",
                                              UriKind.Absolute),
                                          FileHash.Sha1(obj.Value.Hash)));
        }

        foreach (var locked in Context.Lock.Packages)
        {
            if (locked.Rule.Skipping || locked.SuppressedBy is not null)
            {
                continue;
            }

            var parsed = PackageHelper.Parse(locked.Pref);
            var sourcePath = PathDef.Default.FileOfPackageObject(parsed.Repository,
                                                                 parsed.Namespace,
                                                                 parsed.Identity,
                                                                 locked.Resolved.VersionId,
                                                                 Path.GetExtension(locked.Resolved.FileName));
            var targetPath = Path.Combine(PathDef.Default.DirectoryOfBuild(Context.Key), locked.RelativeTarget());
            manifest.FragileFiles.Add(new(sourcePath, targetPath, locked.Resolved.Download, locked.Resolved.Hash));
        }

        var nativesDir = PathDef.Default.DirectoryOfNatives(Context.Key);
        var libraries = artifact.AllLibraries().Concat(artifact.Agents.Select(x => x.Library));
        foreach (var file in libraries.GroupBy(x => x.FilePath(Context.Key), FileHelper.PathComparer))
        {
            var library = file.FirstOrDefault(x => x.Hash is not null) ?? file.First();
            if (library.LocalPath is not null)
            {
                if (!FileHelper.VerifyModified(file.Key, null, library.Hash))
                {
                    throw new InvalidDataException($"Local library '{library.LocalPath}' is missing or changed during deployment.");
                }
            }
            else
            {
                manifest.PresentFiles.Add(new(file.Key, library.Url ?? throw new InvalidDataException("Library has no source."), library.Hash));
            }
            foreach (var native in file.Where(x => x.IsNative))
            {
                manifest.ExplosiveFiles.Add(new(file.Key, nativesDir) { Exclude = native.Exclude });
            }
        }

        if (Context.Runtime != null)
        {
            var dir = PathDef.Default.DirectoryOfRuntime(Context.Runtime.Major);
            foreach (var entry in Context.Runtime.Files)
            {
                manifest.PresentFiles.Add(new(Path.Combine(dir, entry.Path),
                                              entry.Download,
                                              entry.Hash,
                                              entry.IsExecutable));
            }

            foreach (var entry in Context.Runtime.Links)
            {
                manifest.PersistentFiles.Add(new(Path.Combine(dir, entry.Path),
                                                 Path.Combine(dir, entry.Target),
                                                 true,
                                                 false));
            }
        }

        var buildDir = PathDef.Default.DirectoryOfBuild(Context.Key);
        var importDir = PathDef.Default.DirectoryOfImport(Context.Key);
        var persistDir = PathDef.Default.DirectoryOfPersist(Context.Key);
        var set = new Dictionary<string, EntityManifest.PersistentFile>();
        PopulatePersistent(set, importDir, importDir, buildDir, false);
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

    private async ValueTask<MinecraftAssetIndex?> GetAssetIndexAsync(string indexFile, Uri url, FileHash? hash)
    {
        if (File.Exists(indexFile) && hash is not null)
        {
            if (FileHelper.VerifyModified(indexFile, null, hash))
            {
                await using var reader = File.OpenRead(indexFile);
                return await JsonSerializer
                            .DeserializeAsync<MinecraftAssetIndex>(reader, JsonSerializerOptions.Web)
                            .ConfigureAwait(false);
            }
        }

        using var client = factory.CreateClient(RepositoryAgent.CLIENT_NAME);
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
