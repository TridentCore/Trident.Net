using Microsoft.Extensions.Logging;
using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.LaunchPlans;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Models.PrismLauncherApi;
using TridentCore.Core.Services;
using FileHash = TridentCore.Abstractions.Utilities.FileHash;

namespace TridentCore.Core.Engines.Deploying.Stages;

public class ProcessLoaderStage(
    ILogger<ProcessLoaderStage> logger,
    PrismLauncherService prismLauncherService) : StageBase
{
    protected override async Task OnProcessAsync(CancellationToken token)
    {
        // NOTE: 平台未变 → 整个 launch plan（含 loader）已被 InstallVanilla 迁移。
        if (Context.CanReuseLaunchPlan)
        {
            logger.LogInformation("Platform unchanged, loader migrated with launch plan");
            return;
        }

        var loader = Context.Setup.Loader;
        logger.LogInformation("Process loader: {}", loader ?? "(None)");
        if (loader == null)
        {
            return;
        }

        if (!LoaderHelper.TryParse(loader, out var parsed))
        {
            throw new FormatException($"{loader} is not well formatted loader string");
        }

        var plan = Context.LaunchPlan
                 ?? throw new InvalidOperationException("Launch plan missing before loader processing");

        switch (parsed.Identity)
        {
            case LoaderHelper.LOADERID_FORGE:
                await InstallForgeAsync(plan, PrismLauncherService.UID_FORGE, parsed.Version, token)
                   .ConfigureAwait(false);
                break;

            case LoaderHelper.LOADERID_NEOFORGE:
                await InstallForgeAsync(plan, PrismLauncherService.UID_NEOFORGE, parsed.Version, token)
                   .ConfigureAwait(false);
                break;

            case LoaderHelper.LOADERID_FABRIC:
                await InstallFabricAsync(plan, PrismLauncherService.UID_FABRIC, parsed.Version, token)
                   .ConfigureAwait(false);
                break;

            case LoaderHelper.LOADERID_QUILT:
                await InstallFabricAsync(plan, PrismLauncherService.UID_QUILT, parsed.Version, token)
                   .ConfigureAwait(false);
                break;

            default:
                throw new FormatException($"{parsed.Identity} is not known loader");
        }
    }

    private async Task InstallForgeAsync(LaunchPlan plan, string uid, string version, CancellationToken token)
    {
        var index = await prismLauncherService.GetVersionAsync(uid, version, token).ConfigureAwait(false);
        PrismLauncherService.AddValidatedLibraries(plan, index.Libraries ?? Enumerable.Empty<Component.Library>());

        foreach (var file in index.MavenFiles ?? Enumerable.Empty<Component.Library>())
        {
            if (file.Downloads is { Artifact: { } artifact })
            {
                plan.AddLibrary(new(LibraryHelper.ParseIdentity(file.Name),
                                    artifact.Url,
                                    FileHash.FromSha1(artifact.Sha1),
                                    false,
                                    false));
            }
        }

        if (index.MinecraftArguments is { Length: > 0 })
        {
            plan.ClearGameArguments();
        }

        foreach (var argument in index.MinecraftArguments?.Split(' ') ?? Enumerable.Empty<string>())
        {
            plan.AppendGameArgument(argument);
        }

        foreach (var tweaker in index.Tweakers ?? Enumerable.Empty<string>())
        {
            plan.AppendGameArgument("--tweakClass");
            plan.AppendGameArgument(tweaker);
        }

        plan.AppendJavaArgument("-Dforgewrapper.librariesDir=${library_directory}");

        var resolved = plan.Resolve();
        var installer = resolved.Libraries.FirstOrDefault(x => x.Id.Platform == "installer"
                                                            && x.Id.Namespace == uid
                                                            && x.Id.Name == "forge");
        if (installer is not null)
        {
            plan.AppendJavaArgument(
                $"-Dforgewrapper.installer={PathDef.Default.FileOfLibrary(installer.Id.Namespace,
                                                                            installer.Id.Name,
                                                                            installer.Id.Version,
                                                                            installer.Id.Platform,
                                                                            installer.Id.Extension)}");
        }

        var minecraft = resolved.Libraries.FirstOrDefault(x => x.Id is
        {
            Platform: "client",
            Namespace: "com.mojang",
            Name: "minecraft"
        });
        if (minecraft is not null)
        {
            plan.AppendJavaArgument(
                $"-Dforgewrapper.minecraft={PathDef.Default.FileOfLibrary(minecraft.Id.Namespace,
                                                                            minecraft.Id.Name,
                                                                            minecraft.Id.Version,
                                                                            minecraft.Id.Platform,
                                                                            minecraft.Id.Extension)}");
        }

        plan.SetMainClass(index.MainClass ?? "io.github.zekerzhayard.forgewrapper.installer.Main");
    }

    private async Task InstallFabricAsync(LaunchPlan plan, string uid, string version, CancellationToken token)
    {
        var index = await prismLauncherService.GetVersionAsync(uid, version, token).ConfigureAwait(false);
        PrismLauncherService.AddValidatedLibraries(plan, index.Libraries ?? Enumerable.Empty<Component.Library>());

        var intermediary = await prismLauncherService
                                .GetVersionAsync(PrismLauncherService.UID_INTERMEDIARY, Context.Setup.Version, token)
                                .ConfigureAwait(false);
        PrismLauncherService.AddValidatedLibraries(plan, intermediary.Libraries ?? Enumerable.Empty<Component.Library>());
        plan.SetMainClass(index.MainClass ?? "net.fabricmc.loader.impl.launch.knot.KnotClient");
    }
}
