using Trident.Abstractions;
using Trident.Abstractions.Importers;

namespace Trident.Core.Services;

public class ImporterAgent(IEnumerable<IProfileImporter> importers)
{
    public async Task<ImportedProfileContainer> ImportAsync(CompressedProfilePack pack)
    {
        var importer = importers.FirstOrDefault(x => pack.FileNames.Contains(x.IndexFileName));
        if (importer is not null)
        {
            return await importer.ExtractAsync(pack).ConfigureAwait(false);
        }

        throw new ImporterNotFoundException();
    }

    public async Task ExtractFilesAsync(string key, ImportedProfileContainer container, CompressedProfilePack pack)
    {
        var importDir = PathDef.Default.DirectoryOfImport(key);
        await ExtractFilesAsync(importDir, container.ImportFileNames, pack).ConfigureAwait(false);
        var homeDir = PathDef.Default.DirectoryOfHome(key);
        await ExtractFilesAsync(homeDir, container.HomeFileNames, pack).ConfigureAwait(false);
    }

    private async Task ExtractFilesAsync(
        string baseDir,
        IReadOnlyList<(string Source, string Target)> files,
        CompressedProfilePack pack)
    {
        foreach (var (source, target) in files)
        {
            var to = Path.Combine(baseDir, target);
            var dir = Path.GetDirectoryName(to);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var fromStream = pack.Open(source);
            var file = new FileStream(to, FileMode.Create);
            await fromStream.CopyToAsync(file).ConfigureAwait(false);
            await file.FlushAsync().ConfigureAwait(false);
            file.Close();
            fromStream.Close();
        }
    }
}
