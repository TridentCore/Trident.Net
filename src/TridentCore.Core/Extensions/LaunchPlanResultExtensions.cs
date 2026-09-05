using TridentCore.Abstractions.LaunchPlans;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Igniters;

namespace TridentCore.Core.Extensions;

public static class LaunchPlanResultExtensions
{
    public static Igniter MakeIgniter(this LaunchPlanResult self)
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
            igniter.AddLibrary(LibraryLocation.Of(library));
        }

        igniter.SetMainClass(self.MainClass).SetAssetIndex(self.AssetIndex.Id);

        return igniter;
    }
}
