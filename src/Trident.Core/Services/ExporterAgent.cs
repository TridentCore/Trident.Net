using System.IO.Compression;
using Trident.Abstractions;
using Trident.Abstractions.Exporters;

namespace Trident.Core.Services;

public class ExporterAgent(IEnumerable<IProfileExporter> exporters, ProfileManager profileManager)
{
    public async Task<PackedProfileContainer> ExportAsync(
        string label,
        string key,
        string name,
        string author,
        string version)
    {
        var exporter = exporters.FirstOrDefault(x => x.Label == label);
        if (exporter is not null)
        {
            if (profileManager.TryGetImmutable(key, out var profile))
            {
                var pack = new UncompressedProfilePack(key, profile, name, author, version);
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
            await using var writer = entry.Open();
            await stream.CopyToAsync(writer).ConfigureAwait(false);
        }

        var import = PathDef.Default.DirectoryOfImport(container.Key);
        var dirs = new Queue<string>();
        dirs.Enqueue(import);
        while (dirs.TryDequeue(out var dir))
        {
            foreach (var sub in Directory.GetDirectories(dir))
            {
                dirs.Enqueue(sub);
            }

            foreach (var file in Directory.GetFiles(dir))
            {
                var relative = Path.GetRelativePath(import, file);
                var entry = zip.CreateEntry(Path.Combine(container.OverrideDirectoryName, relative));
                await using var writer = entry.Open();
                await using var reader = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                await reader.CopyToAsync(writer).ConfigureAwait(false);
            }
        }

        zip.Dispose();
        output.Position = 0;
        return output;
    }
}
