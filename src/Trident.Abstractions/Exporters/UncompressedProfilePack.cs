using Trident.Abstractions.FileModels;

namespace Trident.Abstractions.Exporters;

public class UncompressedProfilePack
{
    public UncompressedProfilePack(Profile.Rice setup, string home)
    {
        Setup = setup;
        Home = home;
    }

    public Profile.Rice Setup { get; }
    public string Home { get; }
}
