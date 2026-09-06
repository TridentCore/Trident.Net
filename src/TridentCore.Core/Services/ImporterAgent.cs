using Microsoft.Extensions.Logging;
using TridentCore.Abstractions;
using TridentCore.Abstractions.Importers;
using TridentCore.Abstractions.Launching;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Services;

public class ImporterAgent(IEnumerable<IProfileImporter> importers, ILogger<ImporterAgent> logger)
{
    public async Task<ImportedProfileContainer> ImportAsync(CompressedProfilePack pack)
    {
        var importer = importers.FirstOrDefault(x => x.CanHandle(pack));
        if (importer is not null)
        {
            var container = await importer.ExtractAsync(pack).ConfigureAwait(false);
            Report(container.LaunchDiagnostics);
            return container;
        }

        throw new ImporterNotFoundException();
    }

    // 转换诊断的唯一出口。宿主可以另行呈现，但无论如何都先落日志——静默丢弃格式转换中
    // 无法承载的内容会让用户在游戏启动异常时完全无从追溯。
    private void Report(IReadOnlyList<LaunchDiagnostic>? diagnostics)
    {
        foreach (var diagnostic in diagnostics ?? [])
        {
            if (diagnostic.Level == LaunchDiagnostic.Kind.Error)
            {
                logger.LogError("Import diagnostic ({path}): {message}",
                                diagnostic.Path ?? "launch definition",
                                diagnostic.Message);
            }
            else
            {
                logger.LogWarning("Import diagnostic ({path}): {message}",
                                  diagnostic.Path ?? "launch definition",
                                  diagnostic.Message);
            }
        }
    }

    public async Task<string> ExtractIconAsync(string directory, Uri url, HttpClient client, CancellationToken token)
    {
        await using var reader = await client.GetStreamAsync(url, token).ConfigureAwait(false);
        await using var memory = new MemoryStream();
        await reader.CopyToAsync(memory, token).ConfigureAwait(false);
        memory.Position = 0;
        var name = "icon." + FileHelper.GuessBitmapExtension(memory);
        Directory.CreateDirectory(directory);
        memory.Position = 0;
        await using var writer = new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write);
        await memory.CopyToAsync(writer, token).ConfigureAwait(false);
        await writer.FlushAsync(token).ConfigureAwait(false);
        return name;
    }

    public async Task WriteGeneratedToAsync(
        string baseDir,
        IReadOnlyList<(string Target, byte[] Content)>? files,
        CancellationToken token)
    {
        foreach (var (target, content) in files ?? [])
        {
            token.ThrowIfCancellationRequested();
            var to = Path.Combine(baseDir, target);
            if (!FileHelper.IsInDirectory(to, baseDir))
            {
                throw new InvalidDataException($"Generated file '{target}' escapes the extraction root.");
            }

            await FileHelper.TryWriteToFileAsync(to, content).ConfigureAwait(false);
        }
    }

    // importer 负责声明（包里应有什么、映射到哪），agent 负责现实（条目缺失就跳过），
    // 这样声明可以无条件跟随格式规范书写，不必逐个检查存在性。
    public async Task ExtractToAsync(
        string baseDir,
        IReadOnlyList<(string Source, string Target)> files,
        CompressedProfilePack pack,
        CancellationToken token)
    {
        var present = files.Where(f => pack.LengthOf(f.Source) is not null).ToList();

        foreach (var (_, target) in present)
        {
            var destination = Path.Combine(baseDir, target);
            if (!FileHelper.IsInDirectory(destination, baseDir))
            {
                throw new InvalidDataException($"Archive entry '{target}' escapes the extraction root.");
            }

            if (target.StartsWith(LaunchDefinitionHelper.IMPORT_PREFIX, StringComparison.Ordinal)
             && !FileHelper.IsInDirectory(destination,
                                          Path.Combine(baseDir, LaunchDefinitionHelper.IMPORT_DIRECTORY)))
            {
                throw new InvalidDataException($"Archive entry '{target}' escapes the managed launch source.");
            }
        }

        foreach (var (source, target) in present)
        {
            token.ThrowIfCancellationRequested();
            var to = Path.Combine(baseDir, target);
            var dir = Path.GetDirectoryName(to);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await using var fromStream = pack.Open(source);
            await using var file = new FileStream(to, FileMode.Create);
            await fromStream.CopyToAsync(file, token).ConfigureAwait(false);
            await file.FlushAsync(token).ConfigureAwait(false);
        }
    }
}
