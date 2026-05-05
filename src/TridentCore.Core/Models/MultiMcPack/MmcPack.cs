namespace TridentCore.Core.Models.MultiMcPack;

public record MmcPack(
    int FormatVersion,
    IReadOnlyList<MmcPack.ComponentEntry> Components
)
{
    public record ComponentEntry(
        string Uid,
        string Version
    );
}
