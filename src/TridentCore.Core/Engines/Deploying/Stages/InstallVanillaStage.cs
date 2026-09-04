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
        // WARNING: 缓存命中（平台未变且存在完整 launch plan）→ 原子迁移。vanilla 与 loader 耦合
        //  （Forge 重写 args/mainClass），必须一起走。
        if (Context.CanReuseLaunchPlan && Context.BaseLock?.LaunchPlan is { } cached)
        {
            Context.LaunchPlan = LaunchPlan.FromResult(cached);
            logger.LogInformation("Migrated launch plan from BaseLock (platform unchanged)");
            return;
        }

        logger.LogInformation("Platform changed or no launch plan, rebuilding vanilla");
        await BuildVanillaAsync(token).ConfigureAwait(false);
    }

    private async Task BuildVanillaAsync(CancellationToken token)
    {
        var plan = new LaunchPlan();

        var version = await prismLauncherService
                           .GetVersionAsync(PrismLauncherService.UID_MINECRAFT, Context.Setup.Version, token)
                           .ConfigureAwait(false);
        logger.LogInformation("Got version index {version}({uid})", version.Version, version.Uid);

        var patched = await prismLauncherService.GetPatchedLibraries(version, token).ConfigureAwait(false);
        PrismLauncherService.AddValidatedLibraries(plan, patched);

        logger.LogInformation("Libraries added, refer to launch plan for details");

        if (version.MainJar is { Name: { } name, Downloads.Artifact: { } artifact })
        {
            plan.AddLibrary(new(LibraryHelper.ParseIdentity(name),
                                artifact.Url,
                                FileHash.FromSha1(artifact.Sha1)));
            logger.LogInformation("Client jar appended: {name}", name);
        }
        else
        {
            throw new FormatException("{minecraft_version}/mainJar.downloads.artifact");
        }

        foreach (var arg in version.MinecraftArguments?.Split(' ') ?? Enumerable.Empty<string>())
        {
            plan.AppendGameArgument(arg);
        }

        logger.LogInformation("Game arguments added, refer to launch plan for details");

        if (OperatingSystem.IsMacOS())
        {
            plan.AppendJavaArgument("-XstartOnFirstThread");
        }

        if (OperatingSystem.IsWindows())
        {
            plan.AppendJavaArgument("-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump");
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
            plan.AppendJavaArgument(argument);
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
        plan.AddLibrary(new(aiLibraryId, aiArtifact.DownloadUrl, aiArtifact.Hash, false, false));
        logger.LogInformation("authlib-injector {version} registered as library", aiArtifact.Version);

        plan.SetMainClass(mainClass)
            .SetJavaMajorVersion(firstJreVersion)
            .SetAssetIndex(assetIndex);

        Context.LaunchPlan = plan;
    }
}
