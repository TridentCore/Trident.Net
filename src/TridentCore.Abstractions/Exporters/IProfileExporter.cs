namespace TridentCore.Abstractions.Exporters;

public interface IProfileExporter
{
    string Label { get; }
    bool SupportsLaunchDefinitions { get; }

    Task<PackedProfileContainer> PackAsync(UncompressedProfilePack pack);
}
