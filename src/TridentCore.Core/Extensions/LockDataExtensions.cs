using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Core.Igniters;

namespace TridentCore.Core.Extensions;

public static class LockDataExtensions
{
    public static Igniter MakeIgniter(this LockData self)
    {
        var igniter = new Igniter();

        foreach (var argument in self.GameArguments)
        {
            igniter.AddGameArgument(argument);
        }

        foreach (var argument in self.JavaArguments)
        {
            igniter.AddJvmArgument(argument);
        }

        foreach (var library in self.Libraries.Where(x => x.IsPresent))
        {
            igniter.AddLibrary(
                PathDef.Default.FileOfLibrary(
                    library.Id.Namespace,
                    library.Id.Name,
                    library.Id.Version,
                    library.Id.Platform,
                    library.Id.Extension
                )
            );
        }

        igniter.SetMainClass(self.MainClass).SetAssetIndex(self.AssetIndex.Id);

        return igniter;
    }
}
