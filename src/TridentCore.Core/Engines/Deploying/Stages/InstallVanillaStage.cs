using Microsoft.Extensions.Logging;
using TridentCore.Abstractions.FileModels;
using TridentCore.Core.Extensions;
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
        // Cache hit: platform unchanged and a whole artifact exists → migrate it atomically.
        // vanilla and loader are coupled (Forge rewrites args/mainClass), so they travel together.
        if (Context.BaseLock?.Platform == Context.Lock.Platform && Context.BaseLock.Artifact is { } cached)
        {
            Context.Lock = Context.Lock with { Artifact = cached };
            logger.LogInformation("Migrated artifact from BaseLock (platform unchanged)");
            return;
        }

        logger.LogInformation("Platform changed or no artifact, rebuilding vanilla");
        await BuildVanillaAsync(token).ConfigureAwait(false);
    }

    private async Task BuildVanillaAsync(CancellationToken token)
    {
        var libraries = new List<LockData.Library>();
        var gameArguments = new List<string>();
        var javaArguments = new List<string>();

        var version = await prismLauncherService
                           .GetVersionAsync(PrismLauncherService.UID_MINECRAFT, Context.Setup.Version, token)
                           .ConfigureAwait(false);
        logger.LogInformation("Got version index {version}({uid})", version.Version, version.Uid);

        // Libraries
        var patched = await prismLauncherService.GetPatchedLibraries(version, token).ConfigureAwait(false);
        PrismLauncherService.AddValidatedLibrariesToArtifact(libraries, patched);

        logger.LogInformation("Libraries added, refer to artifact file for details");

        // Main Jar as a Library as well
        if (version.MainJar is { Name: { } name, Downloads.Artifact: { } artifact })
        {
            libraries.AddLibrary(name, artifact.Url, FileHash.FromSha1(artifact.Sha1));
            logger.LogInformation("Client jar appended: {name}", name);
        }
        else
        {
            throw new FormatException("{minecraft_version}/mainJar.downloads.artifact");
        }

        // Game Arguments
        var arguments = version.MinecraftArguments?.Split(' ') ?? Enumerable.Empty<string>();
        foreach (var arg in arguments)
        {
            AddUnique(gameArguments, arg);
        }

        logger.LogInformation("Game arguments added, refer to artifact file for details");

        // Jvm Arguments
        if (OperatingSystem.IsMacOS())
        {
            javaArguments.Add("-XstartOnFirstThread");
        }

        if (OperatingSystem.IsWindows())
        {
            javaArguments
               .Add("-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump");
        }

        javaArguments.AddRange([
            // 由于版本文件不再提供，这里手动生成，还有个 logging，这里就不加了
            "-Djava.library.path=${natives_directory}",
            "-DlibraryDirectory=${library_directory}",
            "-Djna.tmpdir=${natives_directory}",
            "-Dorg.lwjgl.system.SharedLibraryExtractPath=${natives_directory}",
            "-Dio.netty.native.workdir=${natives_directory}",
            "-Dminecraft.launcher.brand=${launcher_name}",
            "-Dminecraft.launcher.version=${launcher_version}",
            // 最大内存
            "-Xmx${jvm_max_memory}",
            "-cp",
            "${classpath}"
        ]);

        logger.LogInformation("Jvm arguments generated, refer to artifact file for details");

        // Java Major Version
        var firstJreVersion = version.CompatibleJavaMajors?.FirstOrDefault() ?? 8u;
        if (firstJreVersion.Equals(0))
        {
            throw new FormatException("{minecraft_version}/compatibleJavaMajors");
        }

        logger.LogInformation("Set java major version compatibility to {major}", firstJreVersion);

        // AssetIndex
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

        // Main Class Path
        var mainClass = version.MainClass ?? "net.minecraft.client.main.Main";
        logger.LogInformation("Set main class path to {mainClass}", mainClass);

        // authlib-injector (always present on disk, only activated via -javaagent at launch)
        var aiArtifact = await authlibInjectorService.GetLatestAsync(token).ConfigureAwait(false);
        var aiLibraryId = AuthlibInjectorService.LibraryIdentity(aiArtifact.Version);
        libraries.AddLibrary(new(aiLibraryId, aiArtifact.DownloadUrl, aiArtifact.Hash, false, false));
        logger.LogInformation("authlib-injector {version} registered as library", aiArtifact.Version);

        Context.Lock = Context.Lock with
        {
            Artifact = new(mainClass, firstJreVersion, gameArguments, javaArguments, libraries, assetIndex)
        };
    }

    private static void AddUnique(List<string> collection, string arg)
    {
        arg = arg.Trim();
        if (!collection.Contains(arg))
        {
            collection.Add(arg);
        }
    }
}
