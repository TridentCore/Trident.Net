using Microsoft.Extensions.Logging;
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
        // NOTE: 平台未变 → 整个启动计划（含 loader）由 ResolveLaunchPlan 迁移，见 InstallVanilla。
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

        var operations = new List<LaunchPlanDocument.Operation>();
        switch (parsed.Identity)
        {
            case LoaderHelper.LOADERID_FORGE:
                await InstallForgeAsync(operations, PrismLauncherService.UID_FORGE, parsed.Version, token)
                   .ConfigureAwait(false);
                break;

            case LoaderHelper.LOADERID_NEOFORGE:
                await InstallForgeAsync(operations, PrismLauncherService.UID_NEOFORGE, parsed.Version, token)
                   .ConfigureAwait(false);
                break;

            case LoaderHelper.LOADERID_FABRIC:
                await InstallFabricAsync(operations, PrismLauncherService.UID_FABRIC, parsed.Version, token)
                   .ConfigureAwait(false);
                break;

            case LoaderHelper.LOADERID_QUILT:
                await InstallFabricAsync(operations, PrismLauncherService.UID_QUILT, parsed.Version, token)
                   .ConfigureAwait(false);
                break;

            default:
                throw new FormatException($"{parsed.Identity} is not known loader");
        }

        Context.PlatformLayers.Add(new(LaunchLayer.Origin.Platform, parsed.Identity, operations));
    }

    private async Task InstallForgeAsync(
        List<LaunchPlanDocument.Operation> operations,
        string uid,
        string version,
        CancellationToken token)
    {
        var index = await prismLauncherService.GetVersionAsync(uid, version, token).ConfigureAwait(false);
        PrismLauncherService.AddValidatedLibraries(operations, index.Libraries ?? Enumerable.Empty<Component.Library>());

        foreach (var file in index.MavenFiles ?? Enumerable.Empty<Component.Library>())
        {
            if (file.Downloads is { Artifact: { } artifact })
            {
                operations.Add(new LaunchPlanDocument.AddLibraryOperation(new(LibraryHelper.ParseIdentity(file.Name),
                                                                             artifact.Url,
                                                                             FileHash.FromSha1(artifact.Sha1),
                                                                             false,
                                                                             false)));
            }
        }

        if (index.MinecraftArguments is { Length: > 0 })
        {
            operations.Add(new LaunchPlanDocument.ClearGameArgumentsOperation());
        }

        foreach (var argument in index.MinecraftArguments?.Split(' ') ?? Enumerable.Empty<string>())
        {
            AppendGameArgument(operations, argument);
        }

        foreach (var tweaker in index.Tweakers ?? Enumerable.Empty<string>())
        {
            operations.Add(new LaunchPlanDocument.AppendGameArgumentOperation("--tweakClass"));
            operations.Add(new LaunchPlanDocument.AppendGameArgumentOperation(tweaker));
        }

        operations.Add(new LaunchPlanDocument.AppendJavaArgumentOperation("-Dforgewrapper.librariesDir=${library_directory}"));

        if (FindLibrary(operations,
                        x => x.Id.Platform == "installer" && x.Id.Namespace == uid && x.Id.Name == "forge") is
            { } installer)
        {
            operations
               .Add(new LaunchPlanDocument
                        .AppendJavaArgumentOperation($"-Dforgewrapper.installer={LibraryLocation.Of(installer)}"));
        }

        if (FindLibrary(operations,
                        x => x.Id is { Platform: "client", Namespace: "com.mojang", Name: "minecraft" }) is
            { } minecraft)
        {
            operations
               .Add(new LaunchPlanDocument
                        .AppendJavaArgumentOperation($"-Dforgewrapper.minecraft={LibraryLocation.Of(minecraft)}"));
        }

        operations.Add(new LaunchPlanDocument.SetMainClassOperation(index.MainClass
                                                                ?? "io.github.zekerzhayard.forgewrapper.installer.Main"));
    }

    private async Task InstallFabricAsync(
        List<LaunchPlanDocument.Operation> operations,
        string uid,
        string version,
        CancellationToken token)
    {
        var index = await prismLauncherService.GetVersionAsync(uid, version, token).ConfigureAwait(false);
        PrismLauncherService.AddValidatedLibraries(operations, index.Libraries ?? Enumerable.Empty<Component.Library>());

        var intermediary = await prismLauncherService
                                .GetVersionAsync(PrismLauncherService.UID_INTERMEDIARY, Context.Setup.Version, token)
                                .ConfigureAwait(false);
        PrismLauncherService.AddValidatedLibraries(operations,
                                                   intermediary.Libraries ?? Enumerable.Empty<Component.Library>());
        operations.Add(new LaunchPlanDocument.SetMainClassOperation(index.MainClass
                                                                ?? "net.fabricmc.loader.impl.launch.knot.KnotClient"));
    }

    // ForgeWrapper 需要 installer 与 client jar 的落盘路径，两者分别由本层与 vanilla 层声明，
    // 所以查询要横跨已产出的层加本层——取最后一次声明，与折叠时后来者胜的仲裁一致。
    private LockData.Library? FindLibrary(
        IEnumerable<LaunchPlanDocument.Operation> pending,
        Func<LockData.Library, bool> predicate) =>
        Context
           .PlatformLayers.SelectMany(x => x.Operations)
           .Concat(pending)
           .OfType<LaunchPlanDocument.AddLibraryOperation>()
           .Select(x => x.Value)
           .LastOrDefault(predicate);

    // NOTE: meta 的参数串按空格切分会产出空片段，而校验拒绝空白参数——生产端在此剔除。
    private static void AppendGameArgument(ICollection<LaunchPlanDocument.Operation> operations, string value)
    {
        value = value.Trim();
        if (value.Length > 0)
        {
            operations.Add(new LaunchPlanDocument.AppendGameArgumentOperation(value));
        }
    }
}
