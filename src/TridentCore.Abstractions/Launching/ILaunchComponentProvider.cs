namespace TridentCore.Abstractions.Launching;

public interface ILaunchComponentProvider
{
    Task<LaunchComponent> ResolveAsync(string id, string version, string minecraftVersion, CancellationToken token);
}
