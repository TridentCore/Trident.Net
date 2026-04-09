using System.IO.Compression;
using Trident.Abstractions;
using Trident.Abstractions.Exporters;
using Trident.Abstractions.Extensions;
using Trident.Abstractions.FileModels;

namespace Trident.Core.Services;

public class ExporterAgent(IEnumerable<IProfileExporter> exporters, ProfileManager profileManager)
{
    public async Task<PackedProfileContainer> ExportAsync(
        PackData options,
        string label,
        string key,
        string name,
        string author,
        string version
    )
    {
        var exporter = exporters.FirstOrDefault(x => x.Label == label);
        if (exporter is not null)
        {
            if (profileManager.TryGetImmutable(key, out var profile))
            {
                if (options.ExcludedTags.Count > 0)
                {
                    var excluded = options.ExcludedTags.ToHashSet();
                    profile = profile.Clone();
                    var toRemove = profile
                        .Setup.Packages.Where(p => p.Tags.Any(t => excluded.Contains(t)))
                        .ToList();
                    foreach (var p in toRemove)
                        profile.Setup.Packages.Remove(p);
                }

                var pack = new UncompressedProfilePack(
                    key,
                    profile,
                    options,
                    name,
                    author,
                    version
                );
                return await exporter.PackAsync(pack).ConfigureAwait(false);
            }

            throw new KeyNotFoundException($"{key} is not a key to the managed profile");
        }

        throw new ExporterNotFoundException(label);
    }

    public async Task<Stream> PackCompressedAsync(PackedProfileContainer container)
    {
        var output = new MemoryStream();
        var zip = new ZipArchive(output, ZipArchiveMode.Create, true);
        foreach (var (name, stream) in container.Attachments)
        {
            var entry = zip.CreateEntry(name);
            await using var writer = await entry.OpenAsync().ConfigureAwait(false);
            await stream.CopyToAsync(writer).ConfigureAwait(false);
        }

        var import = PathDef.Default.DirectoryOfImport(container.Key);
        var dirs = new Queue<string>();
        dirs.Enqueue(import);
        while (dirs.TryDequeue(out var dir))
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var sub in Directory.GetDirectories(dir))
            {
                dirs.Enqueue(sub);
            }

            foreach (var file in Directory.GetFiles(dir))
            {
                var relative = Path.GetRelativePath(import, file);
                var entry = zip.CreateEntry(
                    Path.Combine(container.OverrideDirectoryName, relative)
                );
                await using var writer = await entry.OpenAsync().ConfigureAwait(false);
                await using var reader = new FileStream(
                    file,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read
                );
                await reader.CopyToAsync(writer).ConfigureAwait(false);
            }
        }

        await zip.DisposeAsync().ConfigureAwait(false);
        output.Position = 0;
        return output;
    }
}
