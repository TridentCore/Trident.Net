using TridentCore.Abstractions;
using TridentCore.Abstractions.Importers;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Services;

public class ImporterAgent(IEnumerable<IProfileImporter> importers)
{
    public async Task<ImportedProfileContainer> ImportAsync(CompressedProfilePack pack)
    {
        var importer = importers.FirstOrDefault(x => x.CanHandle(pack));
        if (importer is not null)
        {
            return await importer.ExtractAsync(pack).ConfigureAwait(false);
        }

        throw new ImporterNotFoundException();
    }

    public async Task ExtractFilesAsync(string key, ImportedProfileContainer container, CompressedProfilePack pack)
    {
        await ExtractToAsync(PathDef.Default.DirectoryOfImport(key), container.ImportFileNames, pack, CancellationToken.None)
            .ConfigureAwait(false);
        await ExtractToAsync(PathDef.Default.DirectoryOfHome(key), container.HomeFileNames, pack, CancellationToken.None)
            .ConfigureAwait(false);
    }

    // NOTE: importer 负责声明（包里应有什么、映射到哪），agent 负责现实（条目缺失就跳过），
    //  这样声明可以无条件跟随格式规范书写，不必逐个检查存在性。
    public async Task ExtractToAsync(
        string baseDir,
        IReadOnlyList<(string Source, string Target)> files,
        CompressedProfilePack pack,
        CancellationToken token)
    {
        var present = files.Where(f => pack.LengthOf(f.Source) is not null).ToList();

        foreach (var (_, target) in present)
        {
            if (!FileHelper.IsInDirectory(Path.Combine(baseDir, target), baseDir))
            {
                throw new InvalidDataException($"Archive entry '{target}' escapes the extraction root.");
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
