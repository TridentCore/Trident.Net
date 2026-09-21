using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using TridentCore.Abstractions;
using TridentCore.Abstractions.Extensions;
using TridentCore.Abstractions.Snapshots;
using TridentCore.Core.Engines.Deploying;
using TridentCore.Core.Utilities;
using HashAlgorithm = TridentCore.Abstractions.Utilities.HashAlgorithm;

namespace TridentCore.Core.Services;

public class SnapshotManager(ISnapshotStoreFactory factory, ProfileManager profileManager)
{
    public InstanceSnapshots Open(string key) => new(this, key, factory.Open(key));

    public async Task<(SnapshotInfo Snapshot, IReadOnlyList<ReferenceInfo> References)> TakeAsync(
        string key,
        IProgress<int>? collected,
        IProgress<int>? processed,
        CancellationToken token)
    {
        var channel = Channel.CreateBounded<FileInfo>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
        var totalCollected = 0;
        var totalProcessed = 0;
        var totalSize = 0L;
        var bag = new ConcurrentBag<ReferenceInfo>();
        var setup = profileManager.GetImmutable(key).Setup.Clone();
        ProjectionManifestHelper.Validate(key);

        var home = new DirectoryInfo(PathDef.Default.DirectoryOfHome(key));
        var dirs = new[] { PathDef.Default.DirectoryOfImport(key), PathDef.Default.DirectoryOfPersist(key), PathDef.Default.DirectoryOfPatches(key) };


        var producer = Task.Run(async () =>
                                {
                                    try
                                    {
                                        foreach (var dir in dirs)
                                        {
                                            foreach (var path in DeploymentFileHelper.EnumerateFilesWithoutLinks(dir))
                                            {
                                                var file = new FileInfo(path);
                                                await channel.Writer.WriteAsync(file, token).ConfigureAwait(false);
                                                Interlocked.Increment(ref totalCollected);
                                                if (totalCollected % 1000 == 0)
                                                {
                                                    collected?.Report(totalCollected);
                                                }
                                            }
                                        }

                                        foreach (var file in EnumerateImportProjection(key))
                                        {
                                            await channel.Writer.WriteAsync(file, token).ConfigureAwait(false);
                                            Interlocked.Increment(ref totalCollected);
                                            if (totalCollected % 1000 == 0)
                                            {
                                                collected?.Report(totalCollected);
                                            }
                                        }

                                        foreach (var file in EnumerateProjectionManifests(key))
                                        {
                                            await channel.Writer.WriteAsync(file, token).ConfigureAwait(false);
                                            Interlocked.Increment(ref totalCollected);
                                        }
                                    }
                                    finally
                                    {
                                        channel.Writer.Complete();
                                    }
                                },
                                token);

        var consumerCount = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
        var consumers = Enumerable
                       .Range(0, consumerCount)
                       .Select(_ => Task.Run(async () =>
                                             {
                                                 await foreach (var file in channel
                                                                           .Reader.ReadAllAsync(token)
                                                                           .ConfigureAwait(false))
                                                 {
                                                     if (file.ResolveLinkTarget(false) != null)
                                                     {
                                                         throw new
                                                             InvalidOperationException("The symlink is not supported: "
                                                               + file.FullName);
                                                     }

                                                     var size = file.Length;
                                                     Interlocked.Add(ref totalSize, size);
                                                     await using var reader = File.Open(file.FullName,
                                                         FileMode.Open,
                                                         FileAccess.Read,
                                                         FileShare.Read);
                                                     var hash = await FileHelper
                                                                     .ComputeHashAsync(reader, HashAlgorithm.Sha1)
                                                                     .ConfigureAwait(false);
                                                     var relative = Path.GetRelativePath(home.FullName, file.FullName);
                                                     var lastModified = file.LastWriteTime;
                                                     var attributes = file.Attributes;

                                                     var info = new ReferenceInfo(Guid.NewGuid(),
                                                         hash,
                                                         relative,
                                                         size,
                                                         lastModified,
                                                         attributes);
                                                     bag.Add(info);

                                                     Interlocked.Increment(ref totalProcessed);
                                                     if (totalProcessed % 1000 == 0)
                                                     {
                                                         processed?.Report(totalProcessed);
                                                     }
                                                 }
                                             },
                                             token));
        await Task.WhenAll(consumers.Append(producer)).ConfigureAwait(false);

        var snapshot = new SnapshotInfo(Guid.NewGuid(),
                                        string.Empty,
                                        string.Empty,
                                        setup,
                                        setup.Packages.Count,
                                        totalProcessed,
                                        totalSize,
                                        DateTime.Now);

        if (totalCollected % 1000 != 0)
        {
            collected?.Report(totalCollected);
        }

        if (totalProcessed % 1000 != 0)
        {
            processed?.Report(totalProcessed);
        }

        return (snapshot, [.. bag]);
    }

    public Task CommitAsync(
        ISnapshotStore store,
        string key,
        SnapshotInfo snapshot,
        IReadOnlyList<ReferenceInfo> references,
        IProgress<int>? copied = null,
        CancellationToken token = default) =>
        Task.Run(() =>
                 {
                     var home = PathDef.Default.DirectoryOfHome(key);
                     var processed = 0;

                     foreach (var reference in references)
                     {
                         token.ThrowIfCancellationRequested();

                         var sourcePath = Path.Combine(home, reference.RelativePath);
                         var objectPath = PathDef.Default.FileOfSnapshotObject(key, reference.Hash);

                         if (File.Exists(objectPath))
                         {
                             var existingSize = new FileInfo(objectPath).Length;
                             if (existingSize != reference.Size)
                             {
                                 throw new
                                     InvalidDataException($"Snapshot store corruption: object {reference.Hash} size mismatch (expected {reference.Size}, actual {existingSize})");
                             }

                             processed++;
                             copied?.Report(processed);
                             continue;
                         }

                         var prefix = Path.GetDirectoryName(objectPath)!;
                         Directory.CreateDirectory(prefix);
                         var tempPath = Path.Combine(prefix, $"{Guid.NewGuid():N}.tmp");

                         File.Copy(sourcePath, tempPath);
                         File.Move(tempPath, objectPath, true);

                         processed++;
                         copied?.Report(processed);
                     }

                     store.InsertSnapshot(snapshot, references);
                 },
                 token);

    public Task RestoreAsync(
        ISnapshotStore store,
        string key,
        object snapshotId,
        IProgress<int>? restored = null,
        CancellationToken token = default) =>
        Task.Run(async () =>
                 {
                     token.ThrowIfCancellationRequested();

                    var home = PathDef.Default.DirectoryOfHome(key);
                    // WARNING: GetReferences 对不存在的快照返回空表而非抛错；不先校验，空表会让「未引用→删除」的
                    //  还原语义静默清空 import/persist。
                    _ = store.GetSnapshot(snapshotId)
                        ?? throw new InvalidOperationException($"Snapshot {snapshotId} not found");
                    var references = store.GetReferences(snapshotId);
                    var refByPath = references.ToDictionary(x => x.RelativePath, FileHelper.PathComparer);
                    var processed = 0;
                    var matched = new HashSet<string>(FileHelper.PathComparer);
                    var manifestRelativePaths = EnumerateProjectionManifestPaths(key)
                        .Select(x => Path.GetRelativePath(home, x)).ToHashSet(FileHelper.PathComparer);
                    var manifestReferences = references.Where(x => manifestRelativePaths.Contains(x.RelativePath)).ToArray();
                    var targetImportManifest = ReadSnapshotImportManifest(key, home, refByPath);
                    var targetPersistManifest = ReadSnapshotPersistManifest(key, home, refByPath);
                    var targetImportRelativePaths = ResolveSnapshotImportPaths(key, home, references, targetImportManifest);
                    var projectionFiles = EnumerateImportProjection(key, targetImportRelativePaths).ToArray();
                    var build = PathDef.Default.DirectoryOfBuild(key);
                    var targetImportPaths = targetImportRelativePaths
                        .Select(x => ProjectionManifestHelper.ResolveStoredPath(build, x))
                        .ToHashSet(FileHelper.PathComparer);
                    DeploymentFileHelper.DeleteAllLinks(build, token);

                    void ReconcileFile(FileInfo file)
                    {
                        token.ThrowIfCancellationRequested();
                        var relative = Path.GetRelativePath(home, file.FullName);
                        if (refByPath.TryGetValue(relative, out var reference))
                        {
                            matched.Add(reference.RelativePath);

                            var changed = file.Length != reference.Size;
                            if (!changed)
                            {
                                using var stream = File.OpenRead(file.FullName);
                                var hash = FileHelper.ComputeHash(stream, HashAlgorithm.Sha1);
                                changed = hash != reference.Hash;
                            }

                            if (changed)
                            {
                                File.Copy(PathDef.Default.FileOfSnapshotObject(key, reference.Hash), file.FullName, true);
                            }

                            if (file.Attributes != reference.Attributes)
                            {
                                file.Attributes = reference.Attributes;
                            }

                            if (file.LastWriteTime != reference.LastModifiedAt)
                            {
                                File.SetLastWriteTime(file.FullName, reference.LastModifiedAt);
                            }
                        }
                        else
                        {
                            file.Delete();
                        }

                        processed++;
                        restored?.Report(processed);
                    }

                    // WARNING: build 的 import 投影不能整目录遍历（会碰到包软链接/日志/assets），
                    //  只能以 import 清单为驱动枚举 build 受管路径：在引用里则还原、不在则删。
                    //  它必须先于 import/persist 对账执行：否则 import 里快照之后新增的文件先被删除，
                    //  投影枚举便看不到它，build 里对应的部署副本会残留成孤儿。
                    foreach (var file in projectionFiles)
                    {
                        ReconcileFile(file);
                    }

                    var dirs = new[]
                        {
                            PathDef.Default.DirectoryOfImport(key), PathDef.Default.DirectoryOfPersist(key), PathDef.Default.DirectoryOfPatches(key)
                        };

                    foreach (var dir in dirs)
                    {
                        if (DeploymentFileHelper.LinkTarget(dir) is not null)
                            throw new InvalidDataException($"Managed source directory cannot be a symbolic link: {dir}");
                        if (!Directory.Exists(dir))
                        {
                            continue;
                        }

                        var root = new DirectoryInfo(dir);

                        foreach (var path in DeploymentFileHelper.EnumerateFilesWithoutLinks(dir))
                        {
                            ReconcileFile(new FileInfo(path));
                        }

                        foreach (var d in root
                                         .EnumerateDirectories("*", SearchOption.AllDirectories)
                                         .OrderByDescending(d => d.FullName.Length))
                        {
                            if (!d.EnumerateFileSystemInfos().Any())
                            {
                                d.Delete(false);
                            }
                        }
                    }

                     foreach (var reference in references)
                     {
                         token.ThrowIfCancellationRequested();

                         if (matched.Contains(reference.RelativePath))
                         {
                             continue;
                         }
                         if (manifestRelativePaths.Contains(reference.RelativePath)) continue;

                         var targetPath = Path.Combine(home, reference.RelativePath);
                         if (targetImportPaths.Contains(targetPath) && Directory.Exists(targetPath)
                             && !DeploymentFileHelper.DeleteDirectoryTreeIfEmptyOrLinks(targetPath, build))
                             throw new InvalidDataException($"Import projection path is occupied by an unmanaged directory: {targetPath}");
                         Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

                         var objectPath = PathDef.Default.FileOfSnapshotObject(key, reference.Hash);
                         File.Copy(objectPath, targetPath, false);
                         File.SetAttributes(targetPath, reference.Attributes);
                         File.SetLastWriteTime(targetPath, reference.LastModifiedAt);

                         processed++;
                         restored?.Report(processed);
                     }

                     await RestoreProjectionManifestsAsync(key, targetImportManifest, targetPersistManifest, token).ConfigureAwait(false);
                     processed += manifestReferences.Length;
                     restored?.Report(processed);
                 },
                 token);

    private static ImportProjectionManifest? ReadSnapshotImportManifest(
        string key,
        string home,
        IReadOnlyDictionary<string, ReferenceInfo> references)
    {
        var relative = Path.GetRelativePath(home, PathDef.Default.FileOfImportProjectionManifest(key));
        return references.TryGetValue(relative, out var reference)
            ? ProjectionManifestHelper.ReadImportAt(
                PathDef.Default.FileOfSnapshotObject(key, reference.Hash),
                PathDef.Default.DirectoryOfBuild(key))
            : null;
    }

    private static PersistProjectionManifest? ReadSnapshotPersistManifest(
        string key,
        string home,
        IReadOnlyDictionary<string, ReferenceInfo> references)
    {
        var relative = Path.GetRelativePath(home, PathDef.Default.FileOfPersistProjectionManifest(key));
        return references.TryGetValue(relative, out var reference)
            ? ProjectionManifestHelper.ReadPersistAt(
                PathDef.Default.FileOfSnapshotObject(key, reference.Hash),
                PathDef.Default.DirectoryOfBuild(key))
            : null;
    }

    private static IReadOnlyList<string> ResolveSnapshotImportPaths(
        string key,
        string home,
        IReadOnlyList<ReferenceInfo> references,
        ImportProjectionManifest? manifest)
    {
        if (manifest is not null) return manifest.Files;

        var build = PathDef.Default.DirectoryOfBuild(key);
        return [.. references.Select(x => Path.GetFullPath(Path.Combine(home, x.RelativePath)))
            .Where(x => FileHelper.IsInDirectory(x, build) && !ProjectionManifestHelper.IsReservedProjectionPath(build, x))
            .Select(x => ProjectionManifestHelper.ToStoredPath(build, x))
            .Distinct(FileHelper.PathComparer)];
    }

    private static async Task RestoreProjectionManifestsAsync(
        string key,
        ImportProjectionManifest? imports,
        PersistProjectionManifest? persists,
        CancellationToken token)
    {
        // WARNING: 两份清单生命周期独立并按顺序恢复；第二次提交失败可能留下不同代次，
        //  此处不提供跨文件事务或回滚，下一次部署按磁盘上的两份清单继续求差。
        token.ThrowIfCancellationRequested();
        if (imports is null) ProjectionManifestHelper.DeleteImport(key);
        else await ProjectionManifestHelper.WriteImportAsync(key, imports, token).ConfigureAwait(false);

        token.ThrowIfCancellationRequested();
        if (persists is null) ProjectionManifestHelper.DeletePersist(key);
        else await ProjectionManifestHelper.WritePersistAsync(key, persists, token).ConfigureAwait(false);
    }

    private static IEnumerable<FileInfo> EnumerateImportProjection(string key, IEnumerable<string>? targetRelativePaths = null)
    {
        var buildDir = PathDef.Default.DirectoryOfBuild(key);
        if (!Directory.Exists(buildDir)) yield break;

        var relativePaths = new HashSet<string>(targetRelativePaths ?? [], FileHelper.PathComparer);
        relativePaths.UnionWith(ProjectionManifestHelper.GetImportOwnershipPaths(key));
        foreach (var relative in relativePaths)
        {
            var target = ProjectionManifestHelper.ResolveStoredPath(buildDir, relative);
            if (File.Exists(target) && !DeploymentFileHelper.HasLinkAtOrAbove(target, buildDir)) yield return new(target);
        }
    }

    private static IEnumerable<FileInfo> EnumerateProjectionManifests(string key) =>
        EnumerateProjectionManifestPaths(key).Where(File.Exists).Select(x => new FileInfo(x));

    private static IEnumerable<string> EnumerateProjectionManifestPaths(string key)
    {
        yield return PathDef.Default.FileOfImportProjectionManifest(key);
        yield return PathDef.Default.FileOfPersistProjectionManifest(key);
    }

    #region Nested type: InstanceSnapshots

    public class InstanceSnapshots(SnapshotManager manager, string key, ISnapshotStore store) : IDisposable
    {
        public void Dispose() => store.Dispose();

        public Task<(SnapshotInfo Snapshot, IReadOnlyList<ReferenceInfo> References)> TakeAsync(
            IProgress<int>? collected = null,
            IProgress<int>? processed = null,
            CancellationToken token = default) =>
            manager.TakeAsync(key, collected, processed, token);

        public IReadOnlyList<SnapshotInfo> List() => store.GetSnapshots();

        public IReadOnlyList<ReferenceInfo> GetReferences(object snapshotId) => store.GetReferences(snapshotId);

        public SnapshotInfo? Get(object id) => store.GetSnapshot(id);

        public bool TryGet(object id, [MaybeNullWhen(false)] out SnapshotInfo snapshot)
        {
            snapshot = Get(id);
            return snapshot != null;
        }

        public void Delete(object id) => store.DeleteSnapshot(id);

        public Task CommitAsync(
            SnapshotInfo snapshot,
            IReadOnlyList<ReferenceInfo> references,
            IProgress<int>? copied = null,
            CancellationToken token = default) =>
            manager.CommitAsync(store, key, snapshot, references, copied, token);

        public Task RestoreAsync(
            object snapshotId,
            IProgress<int>? restored = null,
            CancellationToken token = default) =>
            manager.RestoreAsync(store, key, snapshotId, restored, token);
    }

    #endregion
}
