namespace TridentCore.Abstractions.Importers;

public interface IProfilePackSource
{
    IReadOnlyList<string> FileNames { get; }
    Stream Open(string fileName);
    long? LengthOf(string fileName);
}
