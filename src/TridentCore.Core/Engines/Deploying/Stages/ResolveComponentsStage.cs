using TridentCore.Abstractions.Launching;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Services;

namespace TridentCore.Core.Engines.Deploying.Stages;

public sealed class ResolveComponentsStage(LaunchDefinitionResolverService resolver, AuthlibInjectorService authlib) : StageBase
{
    protected override async Task OnProcessAsync(CancellationToken token)
    {
        var resolution = await resolver.ResolveAsync(Context.Setup, Context.Definitions, token).ConfigureAwait(false);
        var injector = await authlib.GetLatestAsync(token).ConfigureAwait(false);
        var support = new LaunchComponent
        {
            Id = "trident.authentication", Version = injector.Version,
            Libraries = [new(new(AuthlibInjectorService.LibraryIdentity(injector.Version), injector.DownloadUrl, injector.Hash),
                              LaunchLibrary.Usage.Required)]
        };
        if (resolution.Components.Any(x => x.Definition.Id == support.Id))
        {
            throw new FormatException($"Component identity '{support.Id}' is reserved for account support");
        }
        Context.Resolution = resolution with
        {
            Components = resolution.Components.Add(new(support, "engine:authentication")),
            Fingerprint = HashHelper.ComputeObjectHash(new { resolution.Fingerprint, Support = support })
        };
    }
}
