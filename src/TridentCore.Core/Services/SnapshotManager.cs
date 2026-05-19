using System.Collections.Concurrent;
using System.Collections.Immutable;
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
        var dir = new DirectoryInfo(PathDef.Default.DirectoryOfHome(key));
        var totalCollected = 0;
        var totalProcessed = 0;
        var totalSize = 0L;
        var bag = new ConcurrentBag<ReferenceInfo>();
        var setup = profileManager.GetImmutable(key).Setup.Clone();

        var producer = Task.Run(async () =>
                                {
                                    if (!dir.Exists)
                                    {
                                        return;
                                    }

                                    try
                                    {
                                        foreach (var file in dir.EnumerateFiles("*.*", SearchOption.AllDirectories))
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
                                                     var relative = Path.GetRelativePath(dir.FullName, file.FullName);
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

        return (snapshot, bag.ToImmutableList());
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

        public SnapshotInfo? GetSnapshot(object id) => store.GetSnapshot(id);

        public bool TryGetSnapshot(object id, [MaybeNullWhen(false)] out SnapshotInfo snapshot)
        {
            snapshot = GetSnapshot(id);
            return snapshot != null;
        }
    }

    #endregion
}
