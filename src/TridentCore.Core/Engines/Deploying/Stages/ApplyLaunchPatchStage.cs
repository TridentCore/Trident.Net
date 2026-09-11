using TridentCore.Abstractions.FileModels;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Engines.Deploying.Stages;

public class ApplyLaunchPatchStage(AuthlibInjectorService authlibInjectorService) : StageBase
{
    protected override async Task OnProcessAsync(CancellationToken token)
    {
        var input = Context.Lock.Artifact!;
        var fingerprint = PatchHelper.Fingerprint(new
        {
            Format = LockData.FORMAT,
            Input = PatchHelper.Fingerprint(input),
            Patch = Context.Patches.Fingerprint("launch")
        });
        if (Context.BaseLock?.Launch is { } cached && cached.Input == fingerprint)
        {
            Context.Lock = Context.Lock with { Artifact = cached.Output, Launch = cached };
            return;
        }

        var artifact = Context.Patches.Apply("launch", input);
        PatchSet.ValidateArtifact(artifact);
        var authlib = await authlibInjectorService.GetLatestAsync(token).ConfigureAwait(false);
        artifact = LibraryHelper.Resolve(artifact with
        {
            Libraries =
            [
                .. artifact.Libraries,
                new(AuthlibInjectorService.LibraryIdentity(authlib.Version), authlib.DownloadUrl, authlib.Hash, false, false)
            ]
        });
        Context.Lock = Context.Lock with { Artifact = artifact, Launch = new(fingerprint, artifact) };
    }
}
