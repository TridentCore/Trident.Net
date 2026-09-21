using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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
using TridentCore.Core.Engines.Launching;
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
    private readonly Lock _gate = new();
    private readonly Dictionary<string, InstanceOperation> _operations = new();
    private readonly ActivityPublisher _activities = new(logger);
    private Task? _stopping;
    private readonly Subject<InstanceScrap> _scraps = new();

    /// <summary>全部实例活动的值流。活动开始、每次进度变化、终态各发一个快照。</summary>
    public IObservable<InstanceActivity> Activities => _activities.Stream;

    /// <summary>
    ///     游戏进程输出。生命周期属于 manager 而非单次活动，消费方无需随活动开始/结束反复订阅。
    /// </summary>
    public IObservable<InstanceScrap> Scraps => _scraps;

    public event EventHandler<IAccount>? AccountUpdated;

    private InstanceOperation Register(InstanceActivity seed)
    {
        lock (_gate)
        {
            if (_stopping is not null)
            {
                throw new InvalidOperationException("Instance operations are stopping");
            }

            if (_operations.ContainsKey(seed.Key))
            {
                throw new InvalidOperationException($"Instance {seed.Key} is operated in progress");
            }

            var operation = new InstanceOperation(seed, _activities, Release, logger);
            _operations.Add(seed.Key, operation);
            return operation;
        }
    }

    private void Release(InstanceOperation operation)
    {
        lock (_gate)
        {
            _operations.Remove(operation.Key);
        }
    }

    private IObservable<InstanceActivity> Start(InstanceActivity seed, Func<InstanceOperation, Task> handler)
    {
        var operation = Register(seed);
        operation.Start(handler);
        return operation.Stream;
    }

    /// <summary>当前活动快照；空闲时为 null。</summary>
    public InstanceActivity? ActivityOf(string key) => OperationOf(key)?.Current;

    private InstanceOperation? OperationOf(string key)
    {
        lock (_gate)
        {
            return _operations.GetValueOrDefault(key);
        }
    }

    public bool IsInUse(string key)
    {
        lock (_gate)
        {
            return _operations.ContainsKey(key);
        }
    }

    /// <summary>任意实例有操作在进行，包括阶段衔接和终态通知。</summary>
    public bool IsInUse()
    {
        lock (_gate)
        {
            return _operations.Count > 0;
        }
    }

    /// <summary>当前全部活动快照的瞬时副本。</summary>
    public IReadOnlyList<InstanceActivity> CurrentActivities
    {
        get
        {
            lock (_gate)
            {
                return _operations.Values.Select(x => x.Current).ToList();
            }
        }
    }

    public void Abort(string key) => OperationOf(key)?.RequestStop(InstanceOperation.StopMode.Abort);

    /// <summary>停止操作并保留已经启动的游戏；尚未启动时取消准备。</summary>
    public void Detach(string key) => OperationOf(key)?.RequestStop(InstanceOperation.StopMode.PreserveProcess);

    /// <summary>停止接受新操作，保留已经启动的游戏，并等待所有现存操作结束。重复调用返回同一任务。</summary>
    public Task StopAsync()
    {
        InstanceOperation[] operations;
        Task stopping;
        lock (_gate)
        {
            if (_stopping is not null)
            {
                return _stopping;
            }

            operations = [.. _operations.Values];
            stopping = _stopping = Task.WhenAll(operations.Select(x => x.Completion));
        }

        foreach (var operation in operations)
        {
            operation.RequestStop(InstanceOperation.StopMode.PreserveProcess);
        }

        return stopping;
    }

    /// <summary>部署成功后接着启动，两阶段共享实例占用和取消控制，活动快照各自保留独立 Id。</summary>
    public IObservable<InstanceActivity> DeployAndLaunch(
        string key,
        DeployOptions deploy,
        LaunchOptions launch,
        IReadOnlyList<(uint? Major, string Home)> javaVault) =>
        Start(new InstanceActivity.Deploying { Key = key, Id = Guid.NewGuid() }, async operation =>
        {
            await DeployCoreAsync(operation, deploy).ConfigureAwait(false);
            operation.BeginPhase(CreateRunningActivity(key, launch));
            operation.Token.ThrowIfCancellationRequested();
            await LaunchCoreAsync(operation, launch, javaVault).ConfigureAwait(false);
        });

    #region Common

    private static async Task<MemoryStream> DownloadFileAsync(
        Uri download,
        ulong size,
        Action<double>? reporter,
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
            reporter?.Invoke(progress);
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

    public IObservable<InstanceActivity> Deploy(
        string key,
        DeployOptions options)
    {
        return Start(new InstanceActivity.Deploying { Key = key, Id = Guid.NewGuid() },
                     operation => DeployCoreAsync(operation, options));
    }

    private async Task DeployCoreAsync(
        InstanceOperation run,
        DeployOptions options)
    {
        logger.LogInformation("Begin deploy {}", run.Key);

        var profile = profileManager.GetImmutable(run.Key);
        var engine = new DeployEngine(run.Key,
                                      profile.Setup,
                                      provider,
                                      new() { FullCheckMode = options.FullCheckMode });

        var watch = Stopwatch.StartNew();
        foreach (var stage in engine)
        {
            if (run.Token.IsCancellationRequested)
            {
                break;
            }

            var current = StageOf(stage);
            run.Mutate<InstanceActivity.Deploying>(x => x with
            {
                CurrentStage = current,
                FileCount = null,
                Progress = new ActivityProgress.Indeterminate(current.ToString())
            });

            // 只有部署执行阶段能报出文件计数，其余阶段保持脉冲。
            if (stage is ExecuteDeploymentStage execute)
            {
                execute
                   .ProgressStream.Subscribe(x => run.Mutate<InstanceActivity.Deploying>(a => a with
                    {
                        FileCount = x,
                        Progress = new ActivityProgress.Determinate(current.ToString(),
                                                                    x.Total == 0 ? 0d : (double)x.Current / x.Total)
                    }))
                   .DisposeWith(execute);
            }

            logger.LogInformation("Enter stage {name}", stage.GetType().Name);
            await stage.ProcessAsync(run.Token).ConfigureAwait(false);
        }

        watch.Stop();
        logger.LogInformation("{key} deployed in {ms}ms", run.Key, watch.ElapsedMilliseconds);
    }

    private static DeployStage StageOf(StageBase stage) =>
        stage switch
        {
            LoadLockStage => DeployStage.LoadLock,
            InstallVanillaStage => DeployStage.InstallVanilla,
            ProcessLoaderStage => DeployStage.ProcessLoader,
            ApplyLaunchPatchStage => DeployStage.ApplyLaunchPatch,
            SyncPackagesStage => DeployStage.SyncPackages,
            PersistLockStage => DeployStage.PersistLock,
            SelectRuntimeStage => DeployStage.SelectRuntime,
            PlanDeploymentStage => DeployStage.PlanDeployment,
            ExecuteDeploymentStage => DeployStage.ExecuteDeployment,
            _ => throw new NotSupportedException($"Unrecognized deploy stage {stage.GetType().Name}")
        };

    #endregion

    #region Launch

    public IObservable<InstanceActivity> Launch(
        string key,
        LaunchOptions options,
        IReadOnlyList<(uint? Major, string Home)> javaVault) =>
        Start(CreateRunningActivity(key, options), operation => LaunchCoreAsync(operation, options, javaVault));

    private static InstanceActivity.Running CreateRunningActivity(string key, LaunchOptions options) => new()
    {
        Key = key,
        Id = Guid.NewGuid(),
        AccountId = options.Account?.Uuid,
        MaxMemory = options.MaxMemory,
        Progress = new ActivityProgress.Indeterminate("Launching")
    };

    private async Task LaunchCoreAsync(
        InstanceOperation operation,
        LaunchOptions options,
        IReadOnlyList<(uint? Major, string Home)> javaVault)
    {
        logger.LogInformation("Begin launch {key}", operation.Key);

        var account = options.Account ?? throw new InvalidOperationException("Account is not provided");
        if (await accountConfigurer.ValidateAndRefreshAsync(account, operation.Token).ConfigureAwait(false))
        {
            AccountUpdated?.Invoke(this, account);
        }

        var lockPath = PathDef.Default.FileOfLockData(operation.Key);
        if (!File.Exists(lockPath))
        {
            throw new LockUnavailableException(operation.Key, lockPath, false);
        }

        var lockData = JsonSerializer.Deserialize<LockData>(
            await File.ReadAllTextAsync(lockPath, operation.Token).ConfigureAwait(false), JsonSerializerOptions.Web);
        if (lockData?.Artifact is not { } artifact)
        {
            throw new InvalidOperationException("Lock is not valid or has no artifact");
        }

        var profile = profileManager.GetImmutable(operation.Key);
        var resolution = JavaHelper.Resolve(artifact.CompatibleJavaMajors, javaVault, lockData.RuntimeMajor);
        JavaHelper.EnsureBundledPresent(resolution);
        var javaHome = resolution.Home;
        var workingDirectory = PathDef.Default.DirectoryOfBuild(operation.Key);
        var igniter = artifact.MakeIgniter(operation.Key);

        operation.Mutate<InstanceActivity.Running>(x => x with
        {
            AccountId = account.Uuid,
            JavaHome = javaHome,
            JavaVersion = resolution.RequestedMajor
        });

        igniter
           .SetJavaHome(javaHome)
           .SetWorkingDirectory(workingDirectory)
           .SetAssetRootDirectory(PathDef.Default.CacheAssetDirectory)
           .SetNativesRootDirectory(PathDef.Default.DirectoryOfNatives(operation.Key))
           .SetLibraryRootDirectory(PathDef.Default.CacheLibraryDirectory)
           .SetLauncherName(options.Brand)
           .SetLauncherVersion(Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Eternal")
           .SetOsName(PlatformHelper.GetOsName())
           .SetOsArch(PlatformHelper.GetOsArch())
           .SetOsVersion(PlatformHelper.GetOsVersion())
           .SetUserUuid(account.Uuid)
           .SetUserType(account.UserType)
           .SetUserName(account.Username)
           .SetUserAccessToken(account.AccessToken)
           .SetVersionName(profile.Setup.Version)
           .SetWindowSize(options.WindowSize)
           .SetMaxMemory(options.MaxMemory)
           .SetCommandWrapperTemplate(options.CommandWrapperTemplate)
           .SetReleaseType(options.Brand);
        if (!string.IsNullOrEmpty(options.QuickConnectAddress))
        {
            igniter.SetQuickConnectAddress(options.QuickConnectAddress);
        }

        foreach (var additional in ArgumentHelper.Tokenize(options.AdditionalArguments))
        {
            igniter.AddJvmArgument(additional);
        }

        await accountConfigurer.ConfigureLaunchAsync(account, new(igniter, lockData, operation.Key), operation.Token)
            .ConfigureAwait(false);
        if (options.Mode == LaunchMode.Debug)
        {
            igniter.Debug();
        }

        var startInfo = igniter.Build();
        Directory.CreateDirectory(workingDirectory);
        operation.Mutate<InstanceActivity.Running>(x => x with { CommandLine = FormatCommandLine(startInfo) });
        if (options.Mode == LaunchMode.Debug)
        {
            await File.WriteAllLinesAsync(Path.Combine(workingDirectory, "trident.launch.dump.txt"),
                                          [startInfo.FileName, .. startInfo.ArgumentList], operation.Token)
                .ConfigureAwait(false);
        }

        var activityId = operation.Current.Id;
        LaunchEngine.Result result;
        await using (var launcher = new LaunchEngine(startInfo, options.Mode == LaunchMode.Managed))
        {
            var started = operation.StartProcess(launcher.Start);
            operation.Mutate<InstanceActivity.Running>(x => x with
            {
                ProcessId = started.ProcessId,
                RunStartedAt = started.StartedAt,
                Progress = new ActivityProgress.None()
            });

            result = await launcher.WaitAsync(scrap => _scraps.OnNext(new(operation.Key, activityId, scrap)),
                                              () => operation.PreserveProcess, operation.Token)
                .ConfigureAwait(false);
            operation.Mutate<InstanceActivity.Running>(x => x with
            {
                Outcome = result.Outcome,
                ExitCode = result.ExitCode
            });
        }

        if (result is { Outcome: LaunchOutcome.Crashed, ExitCode: { } code })
        {
            throw new ProcessFaultedException(code, $"The process has exited with non-zero code {code}");
        }
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

    #endregion

    #region Install

    public IObservable<InstanceActivity> Install(string key, string label, string? ns, string pid, string? vid)
    {
        // 仅在线安装有活动——离线导入无需等待，全在前端进行。

        var reserved = profileManager.RequestKey(key);
        return Start(new InstanceActivity.Installing { Key = reserved.Key, Id = Guid.NewGuid() },
                     operation => InstallCoreAsync(operation, reserved, label, ns, pid, vid));
    }

    private async Task InstallCoreAsync(
        InstanceOperation run,
        ReservedKey key,
        string label,
        string? ns,
        string pid,
        string? vid)
    {
        logger.LogInformation("Begin install package {pref} as {key}",
                              PackageHelper.ToPref(label, ns, pid, vid),
                              key.Key);
        var package = await repositories
                           .ResolveAsync(new(label, ns, pid, vid), Filter.None with { Kind = ResourceKind.Modpack })
                           .ConfigureAwait(false);
        var (pack, container) =
            await DownloadAndImportPackageAsync(key.Key, package, run, run.Token).ConfigureAwait(false);

        logger.LogDebug("{} files collected to extract", container.ImportFileNames.Count);

        await importers.ExtractFilesAsync(key.Key, container, pack).ConfigureAwait(false);

        var reference = container.Profile.Setup.Source;
        run.Mutate<InstanceActivity.Installing>(x => x with { Reference = reference });

        profileManager.Add(key, container.Profile);

        logger.LogInformation("{} added", key.Key);
    }

    #endregion

    #region Update

    public IObservable<InstanceActivity> Update(string key, string label, string? ns, string pid, string vid)
    {
        return Start(new InstanceActivity.Updating { Key = key, Id = Guid.NewGuid() },
                     operation => UpdateCoreAsync(operation, key, label, ns, pid, vid));
    }

    private async Task UpdateCoreAsync(
        InstanceOperation run,
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
        var (pack, container) = await DownloadAndImportPackageAsync(key, package, run, run.Token)
                                   .ConfigureAwait(false);

        logger.LogDebug("{} files collected to extract", container.ImportFileNames.Count);

        var oldSource = profileManager.GetImmutable(key).Setup.Source;
        var preparedProfile = profileManager.PrepareUpdate(key, container.Profile.Setup.Source, container.Profile.Name,
            container.Profile.Setup.Version, container.Profile.Setup.Loader,
            [.. container.Profile.Setup.Packages.Select(x => x.Pref)], container.Profile.Overrides);
        var importDir = PathDef.Default.DirectoryOfImport(key);
        if (DeploymentFileHelper.LinkTarget(importDir) is not null)
            throw new InvalidDataException($"Managed source directory cannot be a symbolic link: {importDir}");
        Directory.CreateDirectory(importDir);
        var buildDir = PathDef.Default.DirectoryOfBuild(key);
        var oldProjectionPaths = ProjectionManifestHelper.GetImportOwnershipPaths(key);

        var token = run.Token;
        var homeDir = PathDef.Default.DirectoryOfHome(key);
        var stagingDir = Path.Combine(homeDir, ".import.staging");
        var liveBackupDir = Path.Combine(homeDir, ".live.backup");
        var oldImportDir = Path.Combine(homeDir, ".import.old");
        var patchStagingHome = Path.Combine(homeDir, ".patches.staging");
        var homeBackupDir = Path.Combine(homeDir, ".home.backup");
        var homePromotionsStarted = false;
        PatchStorageHelper.ImportUpdate? patchUpdate = null;
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
            TryCleanup(patchStagingHome);
            TryCleanup(homeBackupDir);
            Directory.CreateDirectory(stagingDir);

            var homeTmp = presentHomeFiles.Select(f => (f.Source, Target: f.Target + ".tmp")).ToList();
            await importers.ExtractToAsync(stagingDir, container.ImportFileNames, pack, token).ConfigureAwait(false);
            await importers.ExtractToAsync(homeDir, homeTmp, pack, token).ConfigureAwait(false);
            ValidateStaged(stagingDir, container.ImportFileNames, pack);
            ValidateStaged(homeDir, homeTmp, pack);
            await importers.ExtractPatchesAsync(patchStagingHome, container, pack, token).ConfigureAwait(false);
            patchUpdate = await PatchStorageHelper.PrepareImportUpdateAsync(homeDir, patchStagingHome, token).ConfigureAwait(false);

            // Phase 2 — back up old build projections before replacing anything.
            // NOTE: the manifest includes projections whose import source was already removed by the author.
            foreach (var relative in oldProjectionPaths)
            {
                token.ThrowIfCancellationRequested();
                var live = ProjectionManifestHelper.ResolveStoredPath(buildDir, relative);
                if (!File.Exists(live) || DeploymentFileHelper.HasLinkAtOrAbove(live, buildDir)) continue;

                var backup = Path.Combine(liveBackupDir, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Move(live, backup);
            }

            foreach (var (_, target) in presentHomeFiles)
            {
                var original = Path.Combine(homeDir, target);
                if (File.Exists(original))
                {
                    var backup = Path.Combine(homeBackupDir, target);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(original, backup);
                }
            }

            // Phase 3 — commit: atomic directory swap, per-file promotion, and direct import manifest invalidation.
            //  Profile persistence remains last so manifest deletion failures use the existing rollback path.
            Directory.Move(importDir, oldImportDir);
            Directory.Move(stagingDir, importDir);
            patchUpdate.Commit();
            homePromotionsStarted = true;
            foreach (var (_, target) in presentHomeFiles)
            {
                File.Move(Path.Combine(homeDir, target + ".tmp"), Path.Combine(homeDir, target), true);
            }
            // WARNING: 清单失效不单独回滚；删除成功后若 profile 提交失败，下一次部署按无 import 清单建立新基线。
            ProjectionManifestHelper.DeleteImport(key);
            profileManager.CommitUpdate(key, preparedProfile, false);
        }
        catch
        {
            var restored = true;
            void Restore(Action action)
            {
                try
                {
                    action();
                }
                catch (Exception rollbackEx)
                {
                    restored = false;
                    logger.LogWarning(rollbackEx, "Update rollback for {key} left residual files", key);
                }
            }
            Restore(() => patchUpdate?.Rollback());
            Restore(() =>
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
            });
            Restore(() =>
            {
                foreach (var (_, target) in presentHomeFiles)
                {
                    if (homePromotionsStarted)
                    {
                        var backup = Path.Combine(homeBackupDir, target);
                        if (File.Exists(backup))
                        {
                            File.Copy(backup, Path.Combine(homeDir, target), true);
                        }
                        else
                        {
                            File.Delete(Path.Combine(homeDir, target));
                        }
                    }
                    File.Delete(Path.Combine(homeDir, target + ".tmp"));
                }
            });
            if (restored)
            {
                TryCleanup(stagingDir);
                TryCleanup(liveBackupDir);
                TryCleanup(patchStagingHome);
                TryCleanup(homeBackupDir);
            }
            throw;
        }

        // Phase 4 — drop backups. Non-critical: next deploy rebuilds live from the new import.
        TryCleanup(liveBackupDir);
        TryCleanup(oldImportDir);
        TryCleanup(patchStagingHome);
        TryCleanup(homeBackupDir);

        var newSource = container.Profile.Setup.Source;
        run.Mutate<InstanceActivity.Updating>(x => x with { OldSource = oldSource, NewSource = newSource });
        profileManager.OnProfileUpdated(key, profileManager.GetImmutable(key));

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
        InstanceOperation run,
        CancellationToken cancellationToken)
    {
        var size = package.Size;
        logger.LogDebug("Downloading package file {url} sized {size} bytes", package.Download.AbsoluteUri, size);
        using var client = clientFactory.CreateClient();

        var memory = await DownloadFileAsync(package.Download,
                                            size,
                                            p => run.Report(new ActivityProgress.Determinate(null, p)),
                                            client,
                                            cancellationToken)
                        .ConfigureAwait(false);

        logger.LogDebug("Downloaded {length} bytes", memory.Length);

        run.Report(new ActivityProgress.Determinate(null, 1d));
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);

        // 解压与导入无法量化，进度回到脉冲。
        run.Report(new ActivityProgress.Indeterminate(null));
        CompressedProfilePack pack = new(memory) { Reference = package };
        var container = await importers.ImportAsync(pack).ConfigureAwait(false);

        // WARNING: 首次安装时实例目录尚未创建，EnumerateFiles 对缺失目录会抛异常。
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
