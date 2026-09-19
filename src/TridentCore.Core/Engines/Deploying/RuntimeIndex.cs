using TridentCore.Abstractions.Utilities;

namespace TridentCore.Core.Engines.Deploying;

public sealed record RuntimeIndex(uint Major, IReadOnlyList<RuntimeIndex.Entry> Files)
{
    public sealed record Entry(string Path, Uri Download, FileHash Hash, bool Executable);
}
