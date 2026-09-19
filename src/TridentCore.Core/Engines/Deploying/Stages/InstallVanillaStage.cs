using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Extensions;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;
using FileHash = TridentCore.Abstractions.Utilities.FileHash;

namespace TridentCore.Core.Engines.Deploying.Stages;

public class InstallVanillaStage(
    ILogger<InstallVanillaStage> logger,
    PrismLauncherService prismLauncherService) : StageBase
{
    protected override async Task OnProcessAsync(CancellationToken token)
    {
        var fingerprint = LockValidationHelper.VanillaInput(Context.Setup, Context.Patches);
        if (Context.BaseLock?.Vanilla is { } cached && cached.Input == fingerprint)
        {
            Context.Lock = Context.Lock with { Artifact = cached.Output, Vanilla = cached };
            logger.LogInformation("Migrated vanilla region");
            return;
        }

        var original = Context.Patches.HasReplacement("vanilla")
                           ? null
                           : await BuildVanillaAsync(token).ConfigureAwait(false);
        var artifact = Context.Patches.Apply("vanilla", original);
        Context.Lock = Context.Lock with { Artifact = artifact, Vanilla = new(fingerprint, artifact) };
    }

    private async Task<LockData.ArtifactData> BuildVanillaAsync(CancellationToken token)
    {
        var version = await prismLauncherService
                           .GetVersionAsync(PrismLauncherService.UID_MINECRAFT, Context.Setup.Version, token)
                           .ConfigureAwait(false);
        var libraries = new List<LockData.Library>();
        if (!Context.Patches.HasReplacement("vanilla.libraries"))
        {
            var patched = await prismLauncherService.GetPatchedLibraries(version, token).ConfigureAwait(false);
            PrismLauncherService.AddValidatedLibrariesToArtifact(libraries, patched);
        }

        if (version.MainJar is not { Name: { } name, Downloads.Artifact: { } main })
        {
            throw new FormatException("{minecraft_version}/mainJar.downloads.artifact");
        }
        var majors = version.CompatibleJavaMajors is { Count: > 0 } declared ? declared : [8u];
        if (majors.Any(x => x == 0))
        {
            throw new FormatException("{minecraft_version}/compatibleJavaMajors");
        }
        if (version.AssetIndex is not { } index)
        {
            throw new FormatException("{minecraft_version}/assetIndex");
        }
        return new(version.MainClass ?? "net.minecraft.client.main.Main", [.. majors.Distinct().Order()],
                   ArgumentHelper.GroupArguments(ArgumentHelper.Tokenize(version.MinecraftArguments ?? "")), ArgumentHelper.DefaultJvmArguments(firstThreadOnMacOS: true),
                   libraries, new(index.Id, index.Url, FileHash.FromSha1(index.Sha1)))
        {
            MainJar = new(PatchHelper.ParseIdentity(name), main.Url, FileHash.FromSha1(main.Sha1))
        };
    }
}
