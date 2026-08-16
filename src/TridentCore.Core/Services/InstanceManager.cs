using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Subjects;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TridentCore.Abstractions;
using TridentCore.Abstractions.Accounts;
using TridentCore.Abstractions.Extensions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Importers;
using TridentCore.Abstractions.Repositories;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Abstractions.Tasks;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Engines;
using TridentCore.Core.Engines.Deploying;
using TridentCore.Core.Engines.Deploying.Stages;
using TridentCore.Core.Exceptions;
using TridentCore.Core.Extensions;
using TridentCore.Core.Igniters;
using TridentCore.Core.Services.Instances;
using TridentCore.Core.Services.Profiles;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Services;

public class InstanceManager(
    ILogger<InstanceManager> logger,
    ProfileManager profileManager,
    RepositoryAgent repositories,
    ImporterAgent importers,
    AccountConfigurerAgent accountConfigurer,
    IServiceProvider provider,
    IHttpClientFactory clientFactory)
{
    // NOTE: 主要在 UI 线程增删改查，实际无需线程同步。
    private readonly Dictionary<string, TrackerBase> _trackers = new();
    public event EventHandler<InstallTracker>? InstanceInstalling;
    public event EventHandler<UpdateTracker>? InstanceUpdating;
    public event EventHandler<DeployTracker>? InstanceDeploying;
    public event EventHandler<LaunchTracker>? InstanceLaunching;
    public event EventHandler<IAccount>? AccountUpdated;

    private void TrackerOnCompleted(TrackerBase tracker)
    {
        tracker.Dispose();
        _trackers.Remove(tracker.Key);
    }

    private static string FormatCommandLine(ProcessStartInfo startInfo)
    {
        if (!string.IsNullOrEmpty(startInfo.Arguments))
        {
            return string.Join(' ', QuoteCommandLineArgument(startInfo.FileName), startInfo.Arguments);
        }

        return string.Join(' ',
                           new[] { startInfo.FileName }
                              .Concat(startInfo.ArgumentList)
                              .Select(QuoteCommandLineArgument));
    }

    private static string QuoteCommandLineArgument(string argument)
    {
        if (argument.Length == 0)
        {
            return "\"\"";
        }

        return argument.Any(char.IsWhiteSpace) || argument.Contains('"')
                   ? $"\"{argument.Replace("\"", "\\\"")}\""
                   : argument;
    }

    public bool IsTracking(string key, [MaybeNullWhen(false)] out TrackerBase tracker)
    {
        if (_trackers.TryGetValue(key, out var value))
        {
            tracker = value;
            return true;
        }

        tracker = null;
        return false;
    }

    public bool IsInUse(string key) => _trackers.ContainsKey(key);

    public void DeployAndLaunch(
        string key,
        DeployOptions deploy,
        LaunchOptions launch,
        JavaHomeLocatorDelegate javaHomeLocator)
    {
        if (IsInUse(key))
        {
            throw new InvalidOperationException($"Instance {key} is operated in progress");
        }

        var path = PathDef.Default.FileOfLockData(key);
        var profile = profileManager.GetImmutable(key);
        if (deploy.FastMode && File.Exists(path))
        {
            var existing = JsonSerializer.Deserialize<LockData>(File.ReadAllText(path), JsonSerializerOptions.Web);

            if (existing is { Artifact: not null }
             && existing.Verify(profile.Setup,
                                ViabilityHashHelper.OptionsOf(deploy),
                                ViabilityHashHelper.PriorityOf(profile.Setup)))
            {
                Launch(key, launch, javaHomeLocator);
                return;
            }
        }

        var tracker = new DeployTracker(key,
                                        async t => await DeployCoreAsync((DeployTracker)t, deploy, javaHomeLocator)
                                                      .ConfigureAwait(false),
                                        t =>
                                        {
                                            TrackerOnCompleted(t);
                                            if (t is { State: TrackerState.Finished })
                                            {
                                                Launch(key, launch, javaHomeLocator);
                                            }
                                        });
        _trackers.Add(key, tracker);
        InstanceDeploying?.Invoke(this, tracker);
        tracker.Start();
    }

    #region Common

    private static async Task<MemoryStream> DownloadFileAsync(
        Uri download,
        ulong size,
        Subject<double?>? reporter,
        HttpClient client,
        CancellationToken token)
    {
        await using var stream = await client.GetStreamAsync(download, token).ConfigureAwait(false);
        var memory = new MemoryStream();
        var buffer = new byte[8 * 1024];
        int read;
        var totalRead = 0L;
        do
        {
            read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            await memory.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            totalRead += read;
            var progress = (double)totalRead / size;
            reporter?.OnNext(progress);
        } while (!token.IsCancellationRequested && read > 0);

        memory.Position = 0;
        return memory;
    }

    private static async Task ExtractIconFileAsync(string key, ImportedProfileContainer container, HttpClient client)
    {
        await using var iconReader = await client.GetStreamAsync(container.IconUrl).ConfigureAwait(false);
        await using var iconMemory = new MemoryStream();
        await iconReader.CopyToAsync(iconMemory).ConfigureAwait(false);
        iconMemory.Position = 0;
        var extension = FileHelper.GuessBitmapExtension(iconMemory);
        var iconPath = PathDef.Default.FileOfIcon(key, extension);
        var dir = Path.GetDirectoryName(iconPath);
        if (dir is not null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        iconMemory.Position = 0;
        await using var iconWriter = new FileStream(iconPath, FileMode.Create);
        await iconMemory.CopyToAsync(iconWriter).ConfigureAwait(false);
        await iconWriter.FlushAsync().ConfigureAwait(false);
    }

    #endregion

    #region Deploy

    public DeployTracker Deploy(string key, DeployOptions options, JavaHomeLocatorDelegate javaHomeLocator)
    {
        if (IsInUse(key))
        {
            throw new InvalidOperationException($"Instance {key} is operated in progress");
        }

        var tracker = new DeployTracker(key,
                                        async t => await DeployCoreAsync((DeployTracker)t, options, javaHomeLocator)
                                                      .ConfigureAwait(false),
                                        TrackerOnCompleted);
        _trackers.Add(key, tracker);
        InstanceDeploying?.Invoke(this, tracker);
        tracker.Start();
        return tracker;
    }

    private async Task DeployCoreAsync(
        DeployTracker tracker,
        DeployOptions options,
        JavaHomeLocatorDelegate javaHomeLocator)
    {
        logger.LogInformation("Begin deploy {}", tracker.Key);

        var profile = profileManager.GetImmutable(tracker.Key);
        var engine = new DeployEngine(tracker.Key,
                                      profile.Setup,
                                      provider,
                                      new()
                                      {
                                          FastMode = options.FastMode,
                                          FullCheckMode = options.FullCheckMode
                                      },
                                      HashHelper.ComputeObjectHash(options),
                                      ViabilityHashHelper.PriorityOf(profile.Setup),
                                      javaHomeLocator);

        var watch = Stopwatch.StartNew();
        foreach (var stage in engine)
        {
            if (tracker.Token.IsCancellationRequested)
            {
                break;
            }

            switch (stage)
            {
                case LoadLockStage:
                    tracker.StageStream.OnNext(DeployStage.LoadLock);
                    tracker.CurrentStage = DeployStage.LoadLock;
                    break;
                case InstallVanillaStage:
                    tracker.StageStream.OnNext(DeployStage.InstallVanilla);
                    tracker.CurrentStage = DeployStage.InstallVanilla;
                    break;
                case ProcessLoaderStage:
                    tracker.StageStream.OnNext(DeployStage.ProcessLoader);
                    tracker.CurrentStage = DeployStage.ProcessLoader;
                    break;
                case SyncPackagesStage:
                    tracker.StageStream.OnNext(DeployStage.SyncPackages);
                    tracker.CurrentStage = DeployStage.SyncPackages;
                    break;
                case FlattenPackagesStage:
                    tracker.StageStream.OnNext(DeployStage.FlattenPackages);
                    tracker.CurrentStage = DeployStage.FlattenPackages;
                    break;
                case PersistLockStage:
                    tracker.StageStream.OnNext(DeployStage.PersistLock);
                    tracker.CurrentStage = DeployStage.PersistLock;
                    break;
                case EnsureRuntimeStage:
                    tracker.StageStream.OnNext(DeployStage.EnsureRuntime);
                    tracker.CurrentStage = DeployStage.EnsureRuntime;
                    break;
                case GenerateManifestStage:
                    tracker.StageStream.OnNext(DeployStage.GenerateManifest);
                    tracker.CurrentStage = DeployStage.GenerateManifest;
                    break;
                case SolidifyManifestStage solidifyManifestStage:
                    tracker.StageStream.OnNext(DeployStage.SolidifyManifest);
                    tracker.CurrentStage = DeployStage.SolidifyManifest;
                    solidifyManifestStage
                       .ProgressStream.Subscribe(tracker.ProgressStream)
                       .DisposeWith(solidifyManifestStage);
                    break;
            }

            logger.LogInformation("Enter stage {name}", stage.GetType().Name);
            await stage.ProcessAsync(tracker.Token).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }

        watch.Stop();
        logger.LogInformation("{key} deployed in {ms}ms", tracker.Key, watch.ElapsedMilliseconds);
    }

    #endregion

    #region Launch

    public LaunchTracker Launch(string key, LaunchOptions options, JavaHomeLocatorDelegate javaHomeLocator)
    {
        if (IsInUse(key))
        {
            throw new InvalidOperationException($"Instance {key} is operated in progress");
        }

        var tracker = new LaunchTracker(key,
                                        options,
                                        async t => await LaunchCoreAsync((LaunchTracker)t, options, javaHomeLocator)
                                                      .ConfigureAwait(false),
                                        TrackerOnCompleted);
        _trackers.Add(key, tracker);
        InstanceLaunching?.Invoke(this, tracker);
        tracker.Start();
        return tracker;
    }

    private async Task LaunchCoreAsync(
        LaunchTracker tracker,
        LaunchOptions options,
        JavaHomeLocatorDelegate javaHomeLocator)
    {
        logger.LogInformation("Begin launch {}", tracker.Key);

        if (options.Account == null)
        {
            throw new InvalidOperationException("Account is not provided");
        }

        await ValidateAndRefreshAccountAsync(options, tracker.Token).ConfigureAwait(false);

        var profile = profileManager.GetImmutable(tracker.Key);

        var artifactPath = PathDef.Default.FileOfLockData(tracker.Key);
        var found = File.Exists(artifactPath);
        if (found)
        {
            var lockData =
                JsonSerializer.Deserialize<LockData>(await File
                                                          .ReadAllTextAsync(artifactPath, tracker.Token)
                                                          .ConfigureAwait(false),
                                                     JsonSerializerOptions.Web);

            if (lockData?.Artifact is not { } artifactData)
            {
                throw new InvalidOperationException("Lock is not valid or has no artifact");
            }

            try
            {
                var javaHome = javaHomeLocator(artifactData.JavaMajorVersion).Home;
                var workingDir = PathDef.Default.DirectoryOfBuild(tracker.Key);
                var libraryDir = PathDef.Default.CacheLibraryDirectory;
                var assetDir = PathDef.Default.CacheAssetDirectory;
                var nativeDir = PathDef.Default.DirectoryOfNatives(tracker.Key);
                var igniter = artifactData.MakeIgniter();

                tracker.JavaHome = javaHome;
                tracker.JavaVersion = artifactData.JavaMajorVersion;

                igniter
                   .SetJavaHome(javaHome)
                   .SetWorkingDirectory(workingDir)
                   .SetAssetRootDirectory(assetDir)
                   .SetNativesRootDirectory(nativeDir)
                   .SetLibraryRootDirectory(libraryDir)
                   .SetLauncherName(options.Brand)
                   .SetLauncherVersion(Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Eternal")
                   .SetOsName(PlatformHelper.GetOsName())
                   .SetOsArch(PlatformHelper.GetOsArch())
                   .SetOsVersion(PlatformHelper.GetOsVersion())
                   .SetUserUuid(options.Account.Uuid)
                   .SetUserType(options.Account.UserType)
                   .SetUserName(options.Account.Username)
                   .SetUserAccessToken(options.Account.AccessToken)
                   .SetVersionName(profile.Setup.Version)
                   .SetWindowSize(options.WindowSize)
                   .SetMaxMemory(options.MaxMemory)
                   .SetCommandWrapperTemplate(options.CommandWrapperTemplate)
                   .SetReleaseType(options.Brand);
                if (!string.IsNullOrEmpty(options.QuickConnectAddress))
                {
                    igniter.SetQuickConnectAddress(options.QuickConnectAddress);
                }

                foreach (var additional in options.AdditionalArguments.Split(' '))
                {
                    igniter.AddJvmArgument(additional);
                }

                var launchContext = new AccountConfigurerAgent.LaunchContext(igniter, lockData);
                await accountConfigurer
                     .ConfigureLaunchAsync(options.Account, launchContext, tracker.Token)
                     .ConfigureAwait(false);

                if (options.Mode == LaunchMode.Debug)
                {
                    igniter.Debug();
                }

                var process = igniter.Build();
                var build = PathDef.Default.DirectoryOfBuild(tracker.Key);
                if (!Directory.Exists(build))
                {
                    Directory.CreateDirectory(build);
                }

                tracker.CommandLine = FormatCommandLine(process.StartInfo);

                if (options.Mode == LaunchMode.Debug)
                {
                    await File
                         .WriteAllLinesAsync(Path.Combine(build, "trident.launch.dump.txt"),
                                             [process.StartInfo.FileName, .. process.StartInfo.ArgumentList])
                         .ConfigureAwait(false);
                }

                if (options.Mode == LaunchMode.Managed)
                {
                    tracker.Process = process;
                    var launcher = new LaunchEngine(process);
                    await foreach (var scrap in launcher.WithCancellation(tracker.Token).ConfigureAwait(false))
                    {
                        tracker.ScrapStream.OnNext(scrap);
                    }

                    tracker.ScrapStream.OnCompleted();
                    tracker.Process = null;

                    if (tracker.Token.IsCancellationRequested)
                    {
                        if (!tracker.IsDetaching)
                        {
                            process.Kill();
                        }
                    }
                    else
                    {
                        await process.WaitForExitAsync(tracker.Token).ConfigureAwait(false);

                        if (process.ExitCode != 0)
                        {
                            var code = process.ExitCode;
                            process.Close();
                            throw new ProcessFaultedException(code,
                                                              $"The process has exited with non-zero code {code}");
                        }
                    }

                    process.Close();
                }
                else
                {
                    process.Start();
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Launch failed due to exception: {ex}", e.Message);
                throw;
            }
        }
        else
        {
            throw new LockUnavailableException(tracker.Key, artifactPath, found);
        }
    }

    private async Task ValidateAndRefreshAccountAsync(LaunchOptions options, CancellationToken token)
    {
        if (options.Account is null)
        {
            return;
        }

        var refreshed = await accountConfigurer.ValidateAndRefreshAsync(options.Account, token).ConfigureAwait(false);
        if (refreshed)
        {
            AccountUpdated?.Invoke(this, options.Account);
        }
    }

    #endregion

    #region Install

    public InstallTracker Install(string key, string label, string? ns, string pid, string? vid)
    {
        // NOTE: 仅在线安装有 Tracker——离线导入无需等待，全在前端进行。

        var reserved = profileManager.RequestKey(key);
        var tracker = new InstallTracker(reserved.Key,
                                         async t => await InstallCoreAsync((InstallTracker)t,
                                                                           reserved,
                                                                           label,
                                                                           ns,
                                                                           pid,
                                                                           vid)
                                                       .ConfigureAwait(false),
                                         TrackerOnCompleted);
        _trackers.Add(reserved.Key, tracker);
        InstanceInstalling?.Invoke(this, tracker);
        tracker.Start();
        return tracker;
    }

    private async Task InstallCoreAsync(
        InstallTracker tracker,
        ReservedKey key,
        string label,
        string? ns,
        string pid,
        string? vid)
    {
        logger.LogInformation("Begin install package {pref} as {key}",
                              PackageHelper.ToPref(label, ns, pid, vid),
                              key.Key);
        tracker.ProgressStream.OnNext(null);
        var package = await repositories
                           .ResolveAsync(new(label, ns, pid, vid), Filter.None with { Kind = ResourceKind.Modpack })
                           .ConfigureAwait(false);
        var (pack, container) =
            await DownloadAndImportPackageAsync(key.Key, package, tracker.ProgressStream, tracker.Token)
               .ConfigureAwait(false);

        logger.LogDebug("{} files collected to extract", container.ImportFileNames.Count);

        await importers.ExtractFilesAsync(key.Key, container, pack).ConfigureAwait(false);

        tracker.Reference = container.Profile.Setup.Source;

        profileManager.Add(key, container.Profile);

        logger.LogInformation("{} added", key.Key);
    }

    #endregion

    #region Update

    public UpdateTracker Update(string key, string label, string? ns, string pid, string vid)
    {
        if (IsInUse(key))
        {
            throw new InvalidOperationException($"Instance {key} is operated in progress");
        }

        var tracker = new UpdateTracker(key,
                                        async t => await UpdateCoreAsync((UpdateTracker)t, key, label, ns, pid, vid)
                                                      .ConfigureAwait(false),
                                        TrackerOnCompleted);
        _trackers.Add(key, tracker);
        InstanceUpdating?.Invoke(this, tracker);
        tracker.Start();
        return tracker;
    }

    private async Task UpdateCoreAsync(
        UpdateTracker tracker,
        string key,
        string label,
        string? ns,
        string pid,
        string vid)
    {
        logger.LogInformation("Begin update {key} from package {pref}", key, PackageHelper.ToPref(label, ns, pid, vid));
        var package = await repositories
                           .ResolveAsync(new(label, ns, pid, vid), Filter.None with { Kind = ResourceKind.Modpack })
                           .ConfigureAwait(false);
        var (pack, container) = await DownloadAndImportPackageAsync(key, package, tracker.ProgressStream, tracker.Token)
                                   .ConfigureAwait(false);

        logger.LogDebug("{} files collected to extract", container.ImportFileNames.Count);

        var importDir = PathDef.Default.DirectoryOfImport(key);
        // NOTE: 只要求 import/ 存在——build/ 是 deploy 的产物，未启动过的实例没有它，
        //  Phase 2 对缺失的 build 逐文件 File.Exists 跳过，天然 no-op。
        if (!Directory.Exists(importDir))
        {
            logger.LogWarning("Update of {key} skipped: the instance has no import directory", key);
            return;
        }
        var buildDir = PathDef.Default.DirectoryOfBuild(key);

        var token = tracker.Token;
        var homeDir = PathDef.Default.DirectoryOfHome(key);
        var stagingDir = Path.Combine(homeDir, ".import.staging");
        var liveBackupDir = Path.Combine(homeDir, ".live.backup");
        var oldImportDir = Path.Combine(homeDir, ".import.old");
        // Declared home files may be absent (Trident lists every icon extension); filter to those
        // present so staging, validation, promotion, and rollback all share one consistent set.
        var presentHomeFiles = container.HomeFileNames.Where(f => pack.LengthOf(f.Source) is not null).ToList();

        try
        {
            // Phase 1 — stage new import + home .tmp into disposable dirs, then validate lengths.
            //  Cancel/fail here only touches staging; the live instance is untouched.
            TryCleanup(stagingDir);
            TryCleanup(liveBackupDir);
            TryCleanup(oldImportDir);

            var homeTmp = presentHomeFiles.Select(f => (f.Source, Target: f.Target + ".tmp")).ToList();
            await importers.ExtractToAsync(stagingDir, container.ImportFileNames, pack, token).ConfigureAwait(false);
            await importers.ExtractToAsync(homeDir, homeTmp, pack, token).ConfigureAwait(false);
            ValidateStaged(stagingDir, container.ImportFileNames, pack);
            ValidateStaged(homeDir, homeTmp, pack);

            // Phase 2 — back up old live (build projections of old import) before replacing anything.
            // NOTE: deploy 只补缺失、不覆盖现存（保留玩家改动），所以旧 import 的 build 投影必须由 update 显式清，
            //  不能丢给 deploy；备份是为了失败时把这些带玩家痕迹的 live 副本原样还原。
            foreach (var file in Directory.EnumerateFiles(importDir, "*", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(importDir, file);
                var live = Path.Combine(buildDir, rel);
                if (!File.Exists(live) || File.ResolveLinkTarget(live, false) is not null)
                {
                    continue;
                }

                var backup = Path.Combine(liveBackupDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Move(live, backup);
            }

            // Phase 3 — commit: atomic dir swap + per-file .tmp promotion. Synchronous renames,
            //  no cancellation window between them; a hard crash here is an accepted edge case.
            Directory.Move(importDir, oldImportDir);
            Directory.Move(stagingDir, importDir);
            foreach (var (_, target) in presentHomeFiles)
            {
                File.Move(Path.Combine(homeDir, target + ".tmp"), Path.Combine(homeDir, target), true);
            }
        }
        catch
        {
            // best-effort rollback to the pre-update state; never let cleanup mask the original failure.
            try
            {
                RestoreLive(liveBackupDir, buildDir);
                if (Directory.Exists(oldImportDir))
                {
                    if (Directory.Exists(importDir))
                    {
                        Directory.Delete(importDir, true);
                    }
                    Directory.Move(oldImportDir, importDir);
                }
                foreach (var (_, target) in presentHomeFiles)
                {
                    File.Delete(Path.Combine(homeDir, target + ".tmp"));
                }
            }
            catch (Exception rollbackEx)
            {
                logger.LogWarning(rollbackEx, "Update rollback for {key} left residual files", key);
            }
            TryCleanup(stagingDir);
            TryCleanup(liveBackupDir);
            throw;
        }

        // Phase 4 — drop backups. Non-critical: next deploy rebuilds live from the new import.
        TryCleanup(liveBackupDir);
        TryCleanup(oldImportDir);

        tracker.OldSource = profileManager.GetImmutable(key).Setup.Source;
        tracker.NewSource = container.Profile.Setup.Source;

        profileManager.Update(key,
                              container.Profile.Setup.Source,
                              container.Profile.Name,
                              container.Profile.Setup.Version,
                              container.Profile.Setup.Loader,
                              [.. container.Profile.Setup.Packages.Select(x => x.Pref)],
                              container.Profile.Overrides);

        logger.LogInformation("{key} updated", key);

        void TryCleanup(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch
            {
                // best-effort
            }
        }

        static void ValidateStaged(string baseDir, IReadOnlyList<(string Source, string Target)> files, CompressedProfilePack pack)
        {
            foreach (var (source, target) in files)
            {
                if (pack.LengthOf(source) is not { } expected)
                {
                    continue;
                }

                var staged = Path.Combine(baseDir, target);
                if (!File.Exists(staged) || new FileInfo(staged).Length != expected)
                {
                    throw new InvalidDataException($"Staged file '{target}' is missing or truncated.");
                }
            }
        }

        static void RestoreLive(string backupDir, string buildDir)
        {
            if (!Directory.Exists(backupDir))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(backupDir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var rel = Path.GetRelativePath(backupDir, file);
                    var live = Path.Combine(buildDir, rel);
                    if (!File.Exists(live))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(live)!);
                        File.Move(file, live);
                    }
                }
                catch
                {
                    // best-effort: keep restoring the rest
                }
            }
        }
    }

    private async Task<(CompressedProfilePack Pack, ImportedProfileContainer Container)> DownloadAndImportPackageAsync(
        string key,
        Package package,
        Subject<double?> progressStream,
        CancellationToken cancellationToken)
    {
        var size = package.Size;
        logger.LogDebug("Downloading package file {url} sized {size} bytes", package.Download.AbsoluteUri, size);
        using var client = clientFactory.CreateClient();

        var memory = await DownloadFileAsync(package.Download, size, progressStream, client, cancellationToken)
                        .ConfigureAwait(false);

        logger.LogDebug("Downloaded {length} bytes", memory.Length);

        progressStream.OnNext(1d);
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);

        progressStream.OnNext(null);
        CompressedProfilePack pack = new(memory) { Reference = package };
        var container = await importers.ImportAsync(pack).ConfigureAwait(false);

        // NOTE: 首次安装时实例目录尚未创建，EnumerateFiles 对缺失目录会抛异常。
        var homeDir = PathDef.Default.DirectoryOfHome(key);
        if (container.IconUrl is not null
            && (!Directory.Exists(homeDir) || !Directory.EnumerateFiles(homeDir, "icon.*").Any()))
        {
            await ExtractIconFileAsync(key, container, client).ConfigureAwait(false);
        }

        return (pack, container);
    }

    #endregion
}
