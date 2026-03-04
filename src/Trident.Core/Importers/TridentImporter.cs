using Trident.Abstractions.Importers;

namespace Trident.Core.Importers;

public class TridentImporter : IProfileImporter
{
    public string IndexFileName => "trident.index.json";

    public Task<ImportedProfileContainer> ExtractAsync(CompressedProfilePack pack)
    {
        // https://d3ara1n.atlassian.net/jira/software/projects/POLY/boards/1?selectedIssue=POLY-39
        throw new NotImplementedException();
    }
}
