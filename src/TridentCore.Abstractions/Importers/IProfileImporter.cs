namespace TridentCore.Abstractions.Importers;

public interface IProfileImporter
{
    bool CanHandle(CompressedProfilePack pack);

    Task<ImportedProfileContainer> ExtractAsync(CompressedProfilePack pack);
}
