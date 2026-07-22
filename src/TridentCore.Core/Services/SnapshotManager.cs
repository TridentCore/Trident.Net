using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using TridentCore.Abstractions;
using TridentCore.Abstractions.Extensions;
using TridentCore.Abstractions.Snapshots;
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

        var home = new DirectoryInfo(PathDef.Default.DirectoryOfHome(key));
        var dirs = new[] { PathDef.Default.DirectoryOfImport(key), PathDef.Default.DirectoryOfPersist(key) };


        var producer = Task.Run(async () =>
                                {
                                    try
                                    {
                                        foreach (var dir in dirs)
                                        {
                                            var directory = new DirectoryInfo(dir);
                                            if (!directory.Exists)
                                            {
                                                continue;
                                            }

                                            foreach (var file in directory.EnumerateFiles("*.*",
                                                         SearchOption.AllDirectories))
                                            {
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

        return (snapshot, bag.ToArray());
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
        Task.Run(() =>
                 {
                     token.ThrowIfCancellationRequested();

                     var home = PathDef.Default.DirectoryOfHome(key);
                     var references = store.GetReferences(snapshotId);
                     var refByPath = references.ToDictionary(x => x.RelativePath, StringComparer.OrdinalIgnoreCase);
                     var processed = 0;
                     var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                     var dirs = new[]
                         {
                             PathDef.Default.DirectoryOfImport(key), PathDef.Default.DirectoryOfPersist(key)
                         };

                     foreach (var dir in dirs)
                     {
                         if (!Directory.Exists(dir))
                         {
                             continue;
                         }

                         var root = new DirectoryInfo(dir);

                         foreach (var file in root.EnumerateFiles("*", SearchOption.AllDirectories))
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
                                     var objectPath = PathDef.Default.FileOfSnapshotObject(key, reference.Hash);
                                     File.Copy(objectPath, file.FullName, true);
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

                     // NOTE: build 的 import 投影不能整目录遍历（会碰到包软链接/日志/assets）。
                     // 上面已把 import 还原到快照状态，以此时的 import 清单枚举 build 受管路径：在引用里则还原、不在则删。
                     foreach (var file in EnumerateImportProjection(key))
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
                                 var objectPath = PathDef.Default.FileOfSnapshotObject(key, reference.Hash);
                                 File.Copy(objectPath, file.FullName, true);
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

                     foreach (var reference in references)
                     {
                         token.ThrowIfCancellationRequested();

                         if (matched.Contains(reference.RelativePath))
                         {
                             continue;
                         }

                         var targetPath = Path.Combine(home, reference.RelativePath);
                         Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

                         var objectPath = PathDef.Default.FileOfSnapshotObject(key, reference.Hash);
                         File.Copy(objectPath, targetPath, false);
                         File.SetAttributes(targetPath, reference.Attributes);
                         File.SetLastWriteTime(targetPath, reference.LastModifiedAt);

                         processed++;
                         restored?.Report(processed);
                     }
                 },
                 token);

    private static IEnumerable<FileInfo> EnumerateImportProjection(string key)
    {
        var importDir = PathDef.Default.DirectoryOfImport(key);
        var buildDir = PathDef.Default.DirectoryOfBuild(key);
        if (!Directory.Exists(importDir) || !Directory.Exists(buildDir))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(importDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(importDir, file);
            var target = Path.Combine(buildDir, rel);
            if (File.Exists(target) && File.ResolveLinkTarget(target, false) is null)
            {
                yield return new(target);
            }
        }
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
