using TridentCore.Abstractions.Launching;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Igniters;

namespace TridentCore.Core.Extensions;

public static class CompiledLaunchExtensions
{
    public static Igniter MakeIgniter(this CompiledLaunch launch)
    {
        var igniter = new Igniter();
        var artifacts = launch.Artifacts.ToDictionary(x => x.Id);
        string Bind(string value) => ArtifactHelper.BindReferences(value, id => ArtifactHelper.LocationOf(artifacts[id]));
        foreach (var argument in launch.GameArguments) igniter.AddGameArgument(Bind(argument));
        foreach (var argument in launch.JavaArguments) igniter.AddJvmArgument(Bind(argument));
        foreach (var identity in launch.Classpath) igniter.AddLibrary(ArtifactHelper.LocationOf(artifacts[identity]));
        return igniter.SetMainClass(launch.MainClass).SetAssetIndex(launch.AssetIndex.Id);
    }
}
