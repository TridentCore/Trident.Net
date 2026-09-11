using System.Text.Json;
using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;
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
        await ExtractPatchesAsync(PathDef.Default.DirectoryOfHome(key), container, pack, CancellationToken.None).ConfigureAwait(false);
    }

    public async Task ExtractPatchesAsync(string home, ImportedProfileContainer container, CompressedProfilePack pack, CancellationToken token)
    {
        var root = Path.Combine(home, "patches");
        var previous = await PatchStorageHelper.ReadIndexAtAsync(root, token).ConfigureAwait(false);
        PatchIndex? index = null;
        if (container.Patches.Generated.TryGetValue(PatchStorageHelper.IndexFileName, out var bytes))
        {
            var incoming = JsonSerializer.Deserialize<PackPatchIndex>(bytes, FileHelper.SerializerOptions)
                        ?? throw new InvalidDataException("Patch index is empty.");
            // NOTE: 归档只有导入层，用户层条目始终取自本机现有索引。合并后的索引要先验再写盘——
            //  条目重叠（例如包与本地同名目录）必须在覆盖本地文件之前拒绝。
            index = new PatchIndex { Format = incoming.Format, Import = incoming.Import, Users = previous.Users };
            PatchStorageHelper.ValidateEntries(home, index);
        }
        await ExtractToAsync(root, container.Patches.Files, pack, token).ConfigureAwait(false);
        foreach (var (relative, file) in container.Patches.Generated)
        {
            var path = PatchHelper.ResolvePath(root, relative);
            if (index is not null && relative == PatchStorageHelper.IndexFileName)
            {
                await PatchStorageHelper.WriteJsonAsync(path, index, token).ConfigureAwait(false);
            }
            else
            {
                RequireImportLayer(relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, file, token).ConfigureAwait(false);
            }
        }
        await PatchStorageHelper.LoadDirectoryAsync(home, token).ConfigureAwait(false);

        // NOTE: 归档只能写导入层，用户层永远由本机自己维护。
        static void RequireImportLayer(string path)
        {
            if (!path.Replace('\\', '/').StartsWith("import/", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Modpack patch data can only write to the import layer.");
            }
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

        var destinations = present.Select(x => (x.Source, Destination: PatchHelper.ResolvePath(baseDir, x.Target)))
            .ToList();

        foreach (var (source, to) in destinations)
        {
            token.ThrowIfCancellationRequested();
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
