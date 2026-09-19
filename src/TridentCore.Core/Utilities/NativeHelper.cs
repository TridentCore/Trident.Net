using System.IO.Compression;

namespace TridentCore.Core.Utilities;

public static class NativeHelper
{
    public static async Task ExtractAsync(string directory, IReadOnlyList<Archive> archives, CancellationToken token)
    {
        if (FilePlanningHelper.LinkTarget(directory) is not null)
            throw new InvalidDataException("The managed natives directory cannot be a symbolic link.");
        var opened = new List<ZipArchive>();
        try
        {
            var expected = new Dictionary<string, ZipArchiveEntry>(FileHelper.PathComparer);
            foreach (var archive in archives)
            {
                token.ThrowIfCancellationRequested();
                var zip = ZipFile.OpenRead(archive.SourcePath);
                opened.Add(zip);
                string? root = null;
                var unwrap = archive.Unwrap && ZipArchiveHelper.HasSingleRootDirectory(zip, out root);
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.EndsWith('/') || archive.Exclude.Any(x => entry.FullName.StartsWith(x, StringComparison.Ordinal))) continue;
                    var relative = unwrap ? entry.FullName[(root!.Length + 1)..] : entry.FullName;
                    var target = PatchHelper.ResolvePath(directory, relative);
                    expected[Path.GetRelativePath(directory, target)] = entry;
                }
            }
            if (await IsCurrentAsync(directory, expected, token).ConfigureAwait(false)) return;
            await ReplaceAsync(directory, expected, token).ConfigureAwait(false);
        }
        finally
        {
            foreach (var zip in opened) zip.Dispose();
        }
    }

    private static async Task<bool> IsCurrentAsync(string directory, IReadOnlyDictionary<string, ZipArchiveEntry> expected, CancellationToken token)
    {
        if (!Directory.Exists(directory)) return expected.Count == 0;
        var actual = new HashSet<string>(FileHelper.PathComparer);
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.TryPop(out var current))
        {
            token.ThrowIfCancellationRequested();
            foreach (var entry in new DirectoryInfo(current).EnumerateFileSystemInfos())
            {
                if (entry.LinkTarget is not null) return false;
                if (entry is DirectoryInfo) pending.Push(entry.FullName);
                else actual.Add(Path.GetRelativePath(directory, entry.FullName));
            }
        }
        if (!actual.SetEquals(expected.Keys)) return false;
        foreach (var (relative, entry) in expected)
        {
            var path = PatchHelper.ResolvePath(directory, relative);
            if (new FileInfo(path).Length != entry.Length) return false;
            await using var source = entry.Open();
            await using var target = File.OpenRead(path);
            var left = new byte[81920];
            var right = new byte[left.Length];
            int count;
            while ((count = await source.ReadAsync(left, token).ConfigureAwait(false)) != 0)
            {
                await target.ReadExactlyAsync(right.AsMemory(0, count), token).ConfigureAwait(false);
                if (!left.AsSpan(0, count).SequenceEqual(right.AsSpan(0, count))) return false;
            }
        }
        return true;
    }

    private static async Task ReplaceAsync(string directory, IReadOnlyDictionary<string, ZipArchiveEntry> expected, CancellationToken token)
    {
        var staging = directory + ".staging-" + Guid.NewGuid().ToString("N");
        var backup = directory + ".previous-" + Guid.NewGuid().ToString("N");
        var installed = false;
        try
        {
            Directory.CreateDirectory(staging);
            foreach (var (relative, entry) in expected)
            {
                token.ThrowIfCancellationRequested();
                var target = PatchHelper.ResolvePath(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var source = entry.Open();
                await using var output = File.Create(target);
                await source.CopyToAsync(output, token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(directory)) Directory.Move(directory, backup);
            try
            {
                Directory.Move(staging, directory);
                installed = true;
            }
            catch
            {
                if (Directory.Exists(backup)) Directory.Move(backup, directory);
                throw;
            }
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            if (installed && Directory.Exists(backup)) Directory.Delete(backup, true);
        }
    }

    public sealed record Archive(string SourcePath, bool Unwrap, IReadOnlyList<string> Exclude);
}
