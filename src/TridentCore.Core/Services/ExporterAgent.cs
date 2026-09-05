using System.IO.Compression;
using Microsoft.Extensions.Logging;
using TridentCore.Abstractions;
using TridentCore.Abstractions.Exporters;
using TridentCore.Abstractions.Extensions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.LaunchPlans;

namespace TridentCore.Core.Services;

public class ExporterAgent(
    IEnumerable<IProfileExporter> exporters,
    ProfileManager profileManager,
    ILogger<ExporterAgent> logger)
{
    public async Task<PackedProfileContainer> ExportAsync(
        PackData options,
        string label,
        string key,
        string name,
        string author,
        string version,
        Action<Profile>? purify = null)
    {
        var exporter = exporters.FirstOrDefault(x => x.Label == label);
        if (exporter is not null)
        {
            if (profileManager.TryGetImmutable(key, out var profile))
            {
                // WARNING: Exporter 会直接改 Profile，必须 clone 以免影响原始数据。
                profile = profile.Clone();

                // 给上层一个净化机会——宿主私有概念（如 recipe 分组）在进入导出前
                // 转换为通用表示，消费者侧无需也无法解析。
                purify?.Invoke(profile);

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
                var container = await exporter.PackAsync(pack).ConfigureAwait(false);
                Report(container.Diagnostics);
                return container;
            }

            throw new KeyNotFoundException($"{key} is not a key to the managed profile");
        }

        throw new ExporterNotFoundException(label);
    }

    // 导出诊断的唯一出口，与导入侧对称。Error 表示目标格式无法表达某个启动意图，
    // 导出产物的行为与源实例不一致。
    private void Report(ICollection<LaunchPlanDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Level == LaunchPlanDiagnostic.Kind.Error)
            {
                logger.LogError("Export diagnostic ({path}): {message}",
                                diagnostic.Path ?? "launch plan",
                                diagnostic.Message);
            }
            else
            {
                logger.LogWarning("Export diagnostic ({path}): {message}",
                                  diagnostic.Path ?? "launch plan",
                                  diagnostic.Message);
            }
        }
    }

    public async Task PackCompressedAsync(Stream writer, PackedProfileContainer container)
    {
        // WARNING: 优先级 Attachments > import > Files——import 内同名项替换 Files 列表项目。

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
                continue;
            }

            if (!File.Exists(abs))
            {
                throw new FileNotFoundException(abs);
            }

            var entry = zip.CreateEntry(relative);
            await using var fileWriter = await entry.OpenAsync().ConfigureAwait(false);
            await using var reader = new FileStream(abs, FileMode.Open, FileAccess.Read, FileShare.Read);
            await reader.CopyToAsync(fileWriter).ConfigureAwait(false);
        }
    }
}
