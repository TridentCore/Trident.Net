namespace Trident.Abstractions.Exporters;

public interface IProfileExporter
{
    string Label { get; }

    Task<PackedProfileContainer> PackAsync(UncompressedProfilePack pack);
}
