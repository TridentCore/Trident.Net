using System.IO.Compression;
using TridentCore.Abstractions;
using TridentCore.Abstractions.Exporters;
using TridentCore.Abstractions.Extensions;
using TridentCore.Abstractions.FileModels;

namespace TridentCore.Core.Services;

public class ExporterAgent(IEnumerable<IProfileExporter> exporters, ProfileManager profileManager)
{
    public async Task<PackedProfileContainer> ExportAsync(
        PackData options,
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
                // Exporter 会直接对 Profile 进行修改，所以必须 clone 避免影响原始数据。
                profile = profile.Clone();

                if (options.ExcludedTags.Count > 0)
                {
                    var excluded = options.ExcludedTags.ToHashSet();
                    var toRemove = profile.Setup.Packages.Where(p => p.Tags.Any(excluded.Contains)).ToList();
                    foreach (var p in toRemove)
                    {
                        profile.Setup.Packages.Remove(p);
                    }
                }

                var pack = new UncompressedProfilePack(key, profile, options, name, author, version);
                return await exporter.PackAsync(pack).ConfigureAwait(false);
            }

            throw new KeyNotFoundException($"{key} is not a key to the managed profile");
        }

        throw new ExporterNotFoundException(label);
    }

    public async Task PackCompressedAsync(Stream writer, PackedProfileContainer container)
    {
        // 如果 import 内有同名的会选择替换掉 Files 列表的项目
        // Attachments > import > Files

        var added = new HashSet<string>();
        await using var zip = new ZipArchive(writer, ZipArchiveMode.Create, true);
        foreach (var (name, stream) in container.Attachments)
        {
            var entryPath = name.Replace('\\', '/');
            var entry = zip.CreateEntry(entryPath);
            await using var entryWriter = await entry.OpenAsync().ConfigureAwait(false);
            await stream.CopyToAsync(entryWriter).ConfigureAwait(false);
            added.Add(entryPath);
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
                var entryPath = Path.Combine(container.OverrideDirectoryName, relative).Replace('\\', '/');
                if (added.Contains(entryPath))
                {
                    continue;
                }

                var entry = zip.CreateEntry(entryPath);
                await using var fileWriter = await entry.OpenAsync().ConfigureAwait(false);
                await using var reader = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                await reader.CopyToAsync(fileWriter).ConfigureAwait(false);
                added.Add(entryPath);
            }
        }

        foreach (var (rel, abs) in container.Files)
        {
            var relative = rel.Replace('\\', '/');
            if (added.Contains(relative))
            {
                // 被 import 内的项目替代
                continue;
            }

            if (!File.Exists(abs))
            {
                // 文件不存在直接报错
                throw new FileNotFoundException(abs);
            }

            var entry = zip.CreateEntry(relative);
            await using var fileWriter = await entry.OpenAsync().ConfigureAwait(false);
            await using var reader = new FileStream(abs, FileMode.Open, FileAccess.Read, FileShare.Read);
            await reader.CopyToAsync(fileWriter).ConfigureAwait(false);
        }
    }
}
