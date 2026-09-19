namespace TridentCore.Core.Engines.Deploying;

public sealed record AssetIndex(IReadOnlyDictionary<string, AssetIndex.Entry> Objects)
{
    public sealed record Entry(string Hash, ulong Size);
}
