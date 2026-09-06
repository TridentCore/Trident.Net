using TridentCore.Abstractions.Launching;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Services;

public sealed class PlatformComponentService(PrismLauncherService metadata) : ILaunchComponentProvider
{
    public async Task<LaunchComponent> ResolveAsync(string id, string version, string minecraftVersion, CancellationToken token)
    {
        var source = await metadata.GetVersionAsync(id, version, token).ConfigureAwait(false);
        var conversion = MetadataComponentHelper.Convert(source, id, version, minecraftVersion);
        if (conversion.LocalFiles.Count > 0)
        {
            throw new FormatException($"Remote component '{id}' requires files that are not supplied by its source");
        }
        return conversion.Component;
    }
}
