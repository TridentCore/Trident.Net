using TridentCore.Abstractions.FileModels;

namespace TridentCore.Abstractions.Exporters;

public class UncompressedProfilePack(
    string key,
    Profile profile,
    PackData options,
    string name,
    string author,
    string version
)
{
    public string Key => key;
    public Profile Profile => profile;
    public PackData Options => options;
    public string Name => name;
    public string Author => author;
    public string Version => version;
}
