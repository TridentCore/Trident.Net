using Trident.Abstractions.FileModels;

namespace Trident.Abstractions.Exporters;

public class UncompressedProfilePack(string key, Profile profile, string name, string author, string version)
{
    public string Key => key;
    public Profile Profile => profile;
    public string Name => name;
    public string Author => author;
    public string Version => version;
}
