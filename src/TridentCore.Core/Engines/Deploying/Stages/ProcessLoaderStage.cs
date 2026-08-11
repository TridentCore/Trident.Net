using Microsoft.Extensions.Logging;
using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Extensions;
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
        // NOTE: 平台未变 → 整个 artifact（含 loader）已被 InstallVanilla 迁移。
        if (Context.BaseLock?.Platform == Context.Lock.Platform)
        {
            logger.LogInformation("Platform unchanged, loader migrated with artifact");
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

        var artifact = Context.Lock.Artifact
                    ?? throw new InvalidOperationException("Artifact missing before loader processing");
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

        Context.Lock = Context.Lock with
        {
            Artifact = artifact with
            {
                Libraries = working.Libraries,
                GameArguments = working.GameArguments,
                JavaArguments = working.JavaArguments,
                MainClass = working.MainClass
            }
        };
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

        foreach (var argument in index.MinecraftArguments?.Split(' ') ?? Enumerable.Empty<string>())
        {
            AddUnique(working.GameArguments, argument);
        }

        if (index.Tweakers != null)
        // NOTE: 不确定列表多元素时如何添加——大胆估计不会多个。
        {
            foreach (var tweaker in index.Tweakers)
            {
                AddUnique(working.GameArguments, "--tweakClass");
                AddUnique(working.GameArguments, tweaker);
            }
        }

        AddUnique(working.JavaArguments, "-Dforgewrapper.librariesDir=${library_directory}");

        var installer = working.Libraries.FirstOrDefault(x => x.Id.Platform == "installer"
                                                           && x.Id.Namespace == uid
                                                           && x.Id.Name == "forge");
        if (installer != null)
        {
            AddUnique(working.JavaArguments,
                      $"-Dforgewrapper.installer={PathDef.Default.FileOfLibrary(installer.Id.Namespace, installer.Id.Name, installer.Id.Version, installer.Id.Platform, installer.Id.Extension)}");
        }

        var minecraft = working.Libraries.FirstOrDefault(x => x.Id is
        {
            Platform: "client",
            Namespace: "com.mojang",
            Name: "minecraft"
        });
        if (minecraft != null)
        {
            AddUnique(working.JavaArguments,
                      $"-Dforgewrapper.minecraft={PathDef.Default.FileOfLibrary(minecraft.Id.Namespace, minecraft.Id.Name, minecraft.Id.Version, minecraft.Id.Platform, minecraft.Id.Extension)}");
        }

        // NOTE: 经拦截给 ForgeWrapper 注入主参数；找不到也不报错（报错需新异常类型，不值）。

        working.MainClass = index.MainClass ?? "io.github.zekerzhayard.forgewrapper.installer.Main";
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

    private static void AddUnique(List<string> collection, string arg)
    {
        arg = arg.Trim();
        if (!collection.Contains(arg))
        {
            collection.Add(arg);
        }
    }

    private sealed class WorkingArtifact
    {
        public required List<LockData.Library> Libraries { get; init; }
        public required List<string> GameArguments { get; init; }
        public required List<string> JavaArguments { get; init; }
        public required string MainClass { get; set; }
    }
}
