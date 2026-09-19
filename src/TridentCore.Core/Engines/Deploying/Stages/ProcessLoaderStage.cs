using Microsoft.Extensions.Logging;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Extensions;
using TridentCore.Core.Models.PrismLauncherApi;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;
using FileHash = TridentCore.Abstractions.Utilities.FileHash;

namespace TridentCore.Core.Engines.Deploying.Stages;

public class ProcessLoaderStage(
    ILogger<ProcessLoaderStage> logger,
    PrismLauncherService prismLauncherService) : StageBase
{
    protected override async Task OnProcessAsync(CancellationToken token)
    {
        var artifact = Context.Lock.Artifact
                    ?? throw new InvalidOperationException("Artifact missing before loader processing");
        var fingerprint = LockValidationHelper.LoaderInput(Context.Setup, Context.Patches, artifact);
        if (Context.BaseLock?.Loader is { } cached && cached.Input == fingerprint)
        {
            Context.Lock = Context.Lock with { Artifact = cached.Output, Loader = cached };
            logger.LogInformation("Migrated loader region");
            return;
        }
        if (Context.Patches.HasReplacement("loader") || !Context.Patches.IsLoaderEnabled() || Context.Setup.Loader is null)
        {
            Store(Context.Patches.Apply("loader", artifact));
            return;
        }
        var loader = Context.Setup.Loader;
        if (!LoaderHelper.TryParse(loader, out var parsed))
        {
            throw new FormatException($"{loader} is not well formatted loader string");
        }

        void Store(LockData.ArtifactData output) =>
            Context.Lock = Context.Lock with { Artifact = output, Loader = new(fingerprint, output) };
        var working = new WorkingArtifact
        {
            Libraries = [.. artifact.Libraries],
            GameArguments = [.. artifact.GameArguments],
            JavaArguments = [.. artifact.JavaArguments],
            MainClass = artifact.MainClass
        };

        switch (parsed.Identity)
        {
            case LoaderHelper.LOADERID_FORGE:
                await InstallForgeAsync(working, PrismLauncherService.UID_FORGE, parsed.Version, token)
                   .ConfigureAwait(false);
                break;

            case LoaderHelper.LOADERID_NEOFORGE:
                await InstallForgeAsync(working, PrismLauncherService.UID_NEOFORGE, parsed.Version, token)
                   .ConfigureAwait(false);
                break;

            case LoaderHelper.LOADERID_FABRIC:
                await InstallFabricAsync(working, PrismLauncherService.UID_FABRIC, parsed.Version, token)
                   .ConfigureAwait(false);
                break;

            case LoaderHelper.LOADERID_QUILT:
                await InstallFabricAsync(working, PrismLauncherService.UID_QUILT, parsed.Version, token)
                   .ConfigureAwait(false);
                break;

            default:
                throw new FormatException($"{parsed.Identity} is not known loader");
        }

        Store(Context.Patches.Apply("loader", artifact with
        {
            Libraries = working.Libraries,
            GameArguments = working.GameArguments,
            JavaArguments = working.JavaArguments,
            MainClass = working.MainClass
        }));
    }

    private async Task InstallForgeAsync(WorkingArtifact working, string uid, string version, CancellationToken token)
    {
        var index = await prismLauncherService.GetVersionAsync(uid, version, token).ConfigureAwait(false);

        PrismLauncherService.AddValidatedLibrariesToArtifact(working.Libraries,
                                                             index.Libraries ?? Enumerable.Empty<Component.Library>());

        foreach (var file in index.MavenFiles ?? Enumerable.Empty<Component.Library>())
        {
            if (file.Downloads is { Artifact: { } artifact })
            {
                working.Libraries.AddLibrary(file.Name, artifact.Url, FileHash.FromSha1(artifact.Sha1), false, false);
            }
        }

        if (index.MinecraftArguments is { Length: > 0 })
        {
            working.GameArguments.Clear();
        }

        working.GameArguments.AddRange(ArgumentHelper.GroupArguments(ArgumentHelper.Tokenize(index.MinecraftArguments ?? "")));

        foreach (var tweaker in index.Tweakers ?? [])
        {
            working.GameArguments.Add(["--tweakClass", tweaker]);
        }

        working.MainClass = index.MainClass ?? "io.github.zekerzhayard.forgewrapper.installer.Main";
        if (working.MainClass == "io.github.zekerzhayard.forgewrapper.installer.Main")
        {
            working.JavaArguments.AddRange(ArgumentHelper.ForgeWrapperJvmArguments());
        }
    }

    private async Task InstallFabricAsync(WorkingArtifact working, string uid, string version, CancellationToken token)
    {
        var index = await prismLauncherService.GetVersionAsync(uid, version, token).ConfigureAwait(false);

        PrismLauncherService.AddValidatedLibrariesToArtifact(working.Libraries,
                                                             index.Libraries ?? Enumerable.Empty<Component.Library>());

        var intermediary = await prismLauncherService
                                .GetVersionAsync(PrismLauncherService.UID_INTERMEDIARY, Context.Setup.Version, token)
                                .ConfigureAwait(false);

        PrismLauncherService.AddValidatedLibrariesToArtifact(working.Libraries,
                                                             intermediary.Libraries
                                                          ?? Enumerable.Empty<Component.Library>());

        working.MainClass = index.MainClass ?? "net.fabricmc.loader.impl.launch.knot.KnotClient";
    }

    private sealed class WorkingArtifact
    {
        public required List<LockData.Library> Libraries { get; init; }
        public required List<string[]> GameArguments { get; init; }
        public required List<string[]> JavaArguments { get; init; }
        public required string MainClass { get; set; }
    }
}
