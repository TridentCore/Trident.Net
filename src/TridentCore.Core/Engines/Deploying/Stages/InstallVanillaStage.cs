using Microsoft.Extensions.Logging;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.LaunchPlans;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Services;
using FileHash = TridentCore.Abstractions.Utilities.FileHash;

namespace TridentCore.Core.Engines.Deploying.Stages;

public class InstallVanillaStage(
    ILogger<InstallVanillaStage> logger,
    PrismLauncherService prismLauncherService,
    AuthlibInjectorService authlibInjectorService) : StageBase
{
    protected override async Task OnProcessAsync(CancellationToken token)
    {
        // WARNING: 缓存命中时连 loader 层一起跳过，由 ResolveLaunchPlan 整体迁移旧结果。vanilla 与
        //  loader 耦合（Forge 重写 args/mainClass），必须一起走，否则产出半迁移的计划。
        if (Context.CanReuseLaunchPlan)
        {
            logger.LogInformation("Platform and launch plan unchanged, skipping vanilla rebuild");
            return;
        }

        logger.LogInformation("Platform changed or no launch plan, rebuilding vanilla");
        Context
           .PlatformLayers.Add(new(LaunchLayer.Origin.Platform,
                                   "vanilla",
                                   await BuildVanillaAsync(token).ConfigureAwait(false)));
    }

    private async Task<IReadOnlyList<LaunchPlanDocument.Operation>> BuildVanillaAsync(CancellationToken token)
    {
        var operations = new List<LaunchPlanDocument.Operation>();

        var version = await prismLauncherService
                           .GetVersionAsync(PrismLauncherService.UID_MINECRAFT, Context.Setup.Version, token)
                           .ConfigureAwait(false);
        logger.LogInformation("Got version index {version}({uid})", version.Version, version.Uid);

        var patched = await prismLauncherService.GetPatchedLibraries(version, token).ConfigureAwait(false);
        PrismLauncherService.AddValidatedLibraries(operations, patched);

        logger.LogInformation("Libraries added, refer to launch plan for details");

        if (version.MainJar is { Name: { } name, Downloads.Artifact: { } artifact })
        {
            operations.Add(new LaunchPlanDocument.AddLibraryOperation(new(LibraryHelper.ParseIdentity(name),
                                                                         artifact.Url,
                                                                         FileHash.FromSha1(artifact.Sha1))));
            logger.LogInformation("Client jar appended: {name}", name);
        }
        else
        {
            throw new FormatException("{minecraft_version}/mainJar.downloads.artifact");
        }

        foreach (var arg in version.MinecraftArguments?.Split(' ') ?? Enumerable.Empty<string>())
        {
            AppendGameArgument(operations, arg);
        }

        logger.LogInformation("Game arguments added, refer to launch plan for details");

        if (OperatingSystem.IsMacOS())
        {
            operations.Add(new LaunchPlanDocument.AppendJavaArgumentOperation("-XstartOnFirstThread"));
        }

        if (OperatingSystem.IsWindows())
        {
            operations
               .Add(new LaunchPlanDocument
                        .AppendJavaArgumentOperation("-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump"));
        }

        foreach (var argument in new[]
                 {
                     "-Djava.library.path=${natives_directory}",
                     "-DlibraryDirectory=${library_directory}",
                     "-Djna.tmpdir=${natives_directory}",
                     "-Dorg.lwjgl.system.SharedLibraryExtractPath=${natives_directory}",
                     "-Dio.netty.native.workdir=${natives_directory}",
                     "-Dminecraft.launcher.brand=${launcher_name}",
                     "-Dminecraft.launcher.version=${launcher_version}",
                     "-Xmx${jvm_max_memory}",
                     "-cp",
                     "${classpath}"
                 })
        {
            operations.Add(new LaunchPlanDocument.AppendJavaArgumentOperation(argument));
        }

        logger.LogInformation("Jvm arguments generated, refer to launch plan for details");

        var firstJreVersion = version.CompatibleJavaMajors?.FirstOrDefault() ?? 8u;
        if (firstJreVersion.Equals(0))
        {
            throw new FormatException("{minecraft_version}/compatibleJavaMajors");
        }

        logger.LogInformation("Set java major version compatibility to {major}", firstJreVersion);

        LockData.AssetData assetIndex;
        if (version.AssetIndex is { } index)
        {
            assetIndex = new(index.Id, index.Url, FileHash.FromSha1(index.Sha1));
            logger.LogInformation("Set asset index to {index}", index.Id);
        }
        else
        {
            throw new FormatException("{minecraft_version}/assetIndex");
        }

        var mainClass = version.MainClass ?? "net.minecraft.client.main.Main";
        logger.LogInformation("Set main class path to {mainClass}", mainClass);

        var aiArtifact = await authlibInjectorService.GetLatestAsync(token).ConfigureAwait(false);
        var aiLibraryId = AuthlibInjectorService.LibraryIdentity(aiArtifact.Version);
        operations.Add(new LaunchPlanDocument.AddLibraryOperation(new(aiLibraryId,
                                                                     aiArtifact.DownloadUrl,
                                                                     aiArtifact.Hash,
                                                                     false,
                                                                     false)));
        logger.LogInformation("authlib-injector {version} registered as library", aiArtifact.Version);

        operations.Add(new LaunchPlanDocument.SetMainClassOperation(mainClass));
        operations.Add(new LaunchPlanDocument.SetJavaMajorVersionOperation(firstJreVersion));
        operations.Add(new LaunchPlanDocument.SetAssetIndexOperation(assetIndex));

        return operations;
    }

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
