using System.IO.Compression;
using System.Text.Json;
using TridentCore.Core.Engines.Deploying;
using HashAlgorithm = TridentCore.Abstractions.Utilities.HashAlgorithm;

namespace TridentCore.Core.Utilities;

public static class NativeHelper
{
    private const string INDEX_FILE_NAME = ".trident-natives.json";

    public static async Task ExtractAsync(string directory, IReadOnlyList<EntityManifest.ExplosiveFile> archives, CancellationToken token)
    {
        if (Path.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("The managed natives directory cannot be a symbolic link.");
        }
        var sources = new List<string>();
        foreach (var archive in archives)
        {
            token.ThrowIfCancellationRequested();
            if (!FileHelper.IsPathEquivalent(archive.TargetDirectory, directory))
            {
                throw new InvalidDataException("A native archive must target the managed natives directory.");
            }
            sources.Add(PatchHelper.Fingerprint(new
            {
                Hash = await FileHelper.ComputeHashAsync(archive.SourcePath, HashAlgorithm.Sha256).ConfigureAwait(false),
                archive.Unwrap,
                archive.Exclude
            }));
        }
        var fingerprint = PatchHelper.Fingerprint(new { Format = 1, Sources = sources });
        if (await IsCurrentAsync(directory, fingerprint, token).ConfigureAwait(false))
        {
            return;
        }

        var staging = directory + ".staging-" + Guid.NewGuid().ToString("N");
        var backup = directory + ".previous-" + Guid.NewGuid().ToString("N");
        var installed = false;
        try
        {
            Directory.CreateDirectory(staging);
            foreach (var archive in archives)
            {
                using var zip = ZipFile.OpenRead(archive.SourcePath);
                string? root = null;
                var unwrap = archive.Unwrap && ZipArchiveHelper.HasSingleRootDirectory(zip, out root);
                foreach (var entry in zip.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (entry.FullName.EndsWith('/') || archive.Exclude.Any(x => entry.FullName.StartsWith(x, StringComparison.Ordinal)))
                    {
                        continue;
                    }
                    var relative = unwrap ? entry.FullName[(root!.Length + 1)..] : entry.FullName;
                    if (FileHelper.PathComparer.Equals(relative, INDEX_FILE_NAME))
                    {
                        throw new InvalidDataException("A native archive contains the reserved extraction index.");
                    }
                    var target = PatchHelper.ResolvePath(staging, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await using var source = entry.Open();
                    await using var output = File.Create(target);
                    await source.CopyToAsync(output, token).ConfigureAwait(false);
                }
            }

            var files = new Dictionary<string, string>(FileHelper.PathComparer);
            foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested();
                files.Add(Path.GetRelativePath(staging, file).Replace('\\', '/'),
                          await FileHelper.ComputeHashAsync(file, HashAlgorithm.Sha256).ConfigureAwait(false));
            }
            await PatchStorageHelper.WriteJsonAsync(Path.Combine(staging, INDEX_FILE_NAME), new Extraction(fingerprint, files), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            // NOTE: Keep the extraction index inside the directory being swapped so a failed
            //  replacement can never mark the previous native files as the new result.
            if (Directory.Exists(directory))
            {
                Directory.Move(directory, backup);
            }
            try
            {
                Directory.Move(staging, directory);
                installed = true;
            }
            catch
            {
                if (Directory.Exists(backup))
                {
                    Directory.Move(backup, directory);
                }
                throw;
            }
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, true);
            }
            if (installed && Directory.Exists(backup))
            {
                Directory.Delete(backup, true);
            }
        }
    }

    private static async Task<bool> IsCurrentAsync(string directory, string fingerprint, CancellationToken token)
    {
        var index = Path.Combine(directory, INDEX_FILE_NAME);
        if (!File.Exists(index) || (File.GetAttributes(index) & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }
        Extraction? extraction;
        try
        {
            await using var stream = File.OpenRead(index);
            extraction = await JsonSerializer.DeserializeAsync<Extraction>(stream, FileHelper.SerializerOptions, token).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return false;
        }
        if (extraction?.Input != fingerprint || extraction.Files is null)
        {
            return false;
        }
        var actual = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                              .Select(x => Path.GetRelativePath(directory, x).Replace('\\', '/'))
                              .Where(x => !FileHelper.PathComparer.Equals(x, INDEX_FILE_NAME))
                              .ToHashSet(FileHelper.PathComparer);
        if (!actual.SetEquals(extraction.Files.Keys))
        {
            return false;
        }
        foreach (var (relative, hash) in extraction.Files)
        {
            token.ThrowIfCancellationRequested();
            var path = PatchHelper.ResolvePath(directory, relative);
            if (await FileHelper.ComputeHashAsync(path, HashAlgorithm.Sha256).ConfigureAwait(false) != hash)
            {
                return false;
            }
        }
        return true;
    }

    private sealed record Extraction(string Input, IReadOnlyDictionary<string, string> Files);
}
