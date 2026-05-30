using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Threading.Channels;
using TridentCore.Abstractions;
using TridentCore.Abstractions.Extensions;
using TridentCore.Abstractions.Snapshots;
using TridentCore.Abstractions.Utilities;

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
            FullMode = BoundedChannelFullMode.Wait,
        });
        var totalCollected = 0;
        var totalProcessed = 0;
        var totalSize = 0L;
        var bag = new ConcurrentBag<ReferenceInfo>();
        var setup = profileManager.GetImmutable(key).Setup.Clone();

        var home = new DirectoryInfo(PathDef.Default.DirectoryOfHome(key));
        var dirs = new[]
        {
            PathDef.Default.DirectoryOfLive(key),
            PathDef.Default.DirectoryOfImport(key),
            PathDef.Default.DirectoryOfPersist(key)
        };


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
                                            foreach (var file in directory.EnumerateFiles("*.*", SearchOption.AllDirectories))
                                            {
                                                await channel.Writer.WriteAsync(file, token).ConfigureAwait(false);
                                                Interlocked.Increment(ref totalCollected);
                                                if (totalCollected % 1000 == 0)
                                                {
                                                    collected?.Report(totalCollected);
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        channel.Writer.Complete();
                                    }
                                },
                                token);

        var consumers = Enumerable
                       .Range(0, Environment.ProcessorCount / 2 + 1)
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
                                                     var hash = await SHA1
                                                                     .HashDataAsync(reader, token)
                                                                     .ConfigureAwait(false);
                                                     var relative = Path.GetRelativePath(home.FullName, file.FullName);
                                                     var lastModified = file.LastWriteTime;
                                                     var attributes = file.Attributes;

                                                     var info = new ReferenceInfo(Guid.NewGuid(),
                                                         HashHelper.FlattenHashBytes(hash),
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

    public Task CommitAsync(ISnapshotStore store, string key, SnapshotInfo snapshot, IReadOnlyList<ReferenceInfo> references, IProgress<int>? copied = null)
    {
        return Task.Run(() =>
        {
            var home = PathDef.Default.DirectoryOfHome(key);
            var processed = 0;

            foreach (var reference in references)
            {
                var sourcePath = Path.Combine(home, reference.RelativePath);
                var objectPath = PathDef.Default.FileOfSnapshotObject(key, reference.Hash);

                if (File.Exists(objectPath))
                {
                    var existingSize = new FileInfo(objectPath).Length;
                    if (existingSize != reference.Size)
                    {
                        throw new InvalidDataException(
                            $"Snapshot store corruption: object {reference.Hash} size mismatch (expected {reference.Size}, actual {existingSize})");
                    }

                    processed++;
                    copied?.Report(processed);
                    continue;
                }

                var prefix = Path.GetDirectoryName(objectPath)!;
                Directory.CreateDirectory(prefix);
                var tempPath = Path.Combine(prefix, $"{Guid.NewGuid():N}.tmp");

                File.Copy(sourcePath, tempPath);
                File.Move(tempPath, objectPath, overwrite: true);

                processed++;
                copied?.Report(processed);
            }

            store.InsertSnapshot(snapshot, references);
        });
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

        public Task CommitAsync(SnapshotInfo snapshot, IReadOnlyList<ReferenceInfo> references, IProgress<int>? copied = null) => manager.CommitAsync(store, key, snapshot, references, copied);
    }

    #endregion
}
