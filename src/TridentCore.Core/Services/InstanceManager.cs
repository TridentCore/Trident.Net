using System.Collections.Concurrent;
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
using TridentCore.Core.Facilities;
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
    InstanceModpackService modpacks,
    AccountConfigurerAgent accountConfigurer,
    IServiceProvider provider,
    IHttpClientFactory clientFactory)
{
    // 一个 key 同时只能有一个活动。登记在调用方线程（通常 UI）发生，摧除在工作线程，故需并发安全容器。
    private readonly ConcurrentDictionary<string, ActivityRun> _runs = new();

    private readonly Subject<InstanceActivity> _activities = new();
    private readonly Subject<InstanceScrap> _scraps = new();

    /// <summary>全部实例活动的值流。活动开始、每次进度变化、终态各发一个快照。</summary>
    public IObservable<InstanceActivity> Activities => _activities;

    /// <summary>
    ///     游戏进程输出。生命周期属于 manager 而非单次活动，消费方无需随活动开始/结束反复订阅。
    /// </summary>
    public IObservable<InstanceScrap> Scraps => _scraps;

    public event EventHandler<IAccount>? AccountUpdated;

    /// <summary>登记一次新活动并接入全局流。key 已占用时抛错。</summary>
    private ActivityRun Register(InstanceActivity seed)
    {
        var run = new ActivityRun(seed);
        if (!_runs.TryAdd(seed.Key, run))
        {
            throw new InvalidOperationException($"Instance {seed.Key} is operated in progress");
        }

        // WARNING: 只转发 OnNext。直接把 _activities 当观察者接上会让单个活动的 OnCompleted
        //  终结整个全局流，此后所有实例的状态都不再发出。
        //  ReplaySubject(1) 会在订阅时重放最近值，活动开始自然随之广播。
        run.Stream.Subscribe(x => _activities.OnNext(x));
        return run;
    }

    /// <summary>执行一次活动并落终态。异常全部收枕为终态，不向外抛。</summary>
    internal async Task<InstanceActivity> ExecuteAsync(ActivityRun run, Func<ActivityRun, Task> handler, ReservedKey? reservation = null)
    {
        ActivityState state;
        Exception? reason = null;
        try
        {
            FileTransaction.EnsureInstanceReady(PathDef.Default.DirectoryOfHome(run.Key));
            await handler(run).ConfigureAwait(false);
            state = ActivityState.Finished;
        }
        catch (OperationCanceledException) when (run.Token.IsCancellationRequested)
        {
            state = ActivityState.Cancelled;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Activity {kind} of {key} faulted", run.Current.Kind, run.Key);
            state = ActivityState.Faulted;
            reason = ex;
        }
        finally
        {
            reservation?.Dispose();
        }

        // NOTE: 先摧除再落终态——消费方收到终态值时 IsInUse 必须已为 false，否则串联的下一段
        //  （部署完接着启动）会被占用检查挡住。
        _runs.TryRemove(run.Key, out _);
        run.Complete(state, reason);
        return run.Current;
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

    /// <summary>当前活动快照；空闲时为 null。</summary>
    public InstanceActivity? ActivityOf(string key) => _runs.TryGetValue(key, out var run) ? run.Current : null;

    public bool IsInUse(string key) => _runs.ContainsKey(key);

    /// <summary>中止当前活动。无活动则无操作。</summary>
    public void Abort(string key)
    {
        if (_runs.TryGetValue(key, out var run))
        {
            run.Abort();
        }
    }

    /// <summary>请求分离：中止时不杀进程，让游戏脱离启动器继续运行。</summary>
    public void Detach(string key)
    {
        if (_runs.TryGetValue(key, out var run) && run.Current is InstanceActivity.Running)
        {
            run.Mutate<InstanceActivity.Running>(x => x with { IsDetaching = true });
            run.Abort();
        }
    }

    /// <summary>
    ///     部署成功后接着启动。两段是先后两次活动，各自有独立的 Id。
    /// </summary>
    /// <remarks>
    ///     WARNING: 衔接必须在异步方法内完成。历史上它写在完成回调里，启动阶段抛出的异常
    ///     无人接手，直接终止进程。
    /// </remarks>
    public IObservable<InstanceActivity> DeployAndLaunch(
        string key,
        DeployOptions deploy,
        LaunchOptions launch,
        JavaHomeLocatorDelegate javaHomeLocator)
    {
        var relay = new ReplaySubject<InstanceActivity>(1);
        var deployRun = Register(new InstanceActivity.Deploying { Key = key, Id = Guid.NewGuid() });
        _ = OrchestrateAsync();
        return relay;

        async Task OrchestrateAsync()
        {
            using (deployRun.Stream.Subscribe(relay.OnNext))
            {
                var deployed = await ExecuteAsync(deployRun, r => DeployCoreAsync(r, deploy, javaHomeLocator))
                                  .ConfigureAwait(false);
                if (deployed.State is not ActivityState.Finished)
                {
                    relay.OnCompleted();
                    return;
                }
            }

            try
            {
                var launchRun = Register(new InstanceActivity.Running
                {
                    Key = key,
                    Id = Guid.NewGuid(),
                    Options = launch,
                    Progress = new ActivityProgress.Indeterminate("Launching")
                });
                using (launchRun.Stream.Subscribe(relay.OnNext))
                {
                    await ExecuteAsync(launchRun, r => LaunchCoreAsync(r, launch, javaHomeLocator))
                       .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // 启动阶段未能开始（如 key 被占）——发一个已终结的失败活动，让它走常规通知路径。
                logger.LogError(ex, "Launch phase of {key} could not start", key);
                var failed = new InstanceActivity.Running
                {
                    Key = key,
                    Id = Guid.NewGuid(),
                    Options = launch,
                    State = ActivityState.Faulted,
                    FailureReason = ex
                };
                _activities.OnNext(failed);
                relay.OnNext(failed);
            }

            relay.OnCompleted();
        }
    }

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

    #endregion

    #region Deploy

    public IObservable<InstanceActivity> Deploy(
        string key,
        DeployOptions options,
        JavaHomeLocatorDelegate javaHomeLocator)
    {
        var run = Register(new InstanceActivity.Deploying { Key = key, Id = Guid.NewGuid() });
        _ = ExecuteAsync(run, r => DeployCoreAsync(r, options, javaHomeLocator));
        return run.Stream;
    }

    private async Task DeployCoreAsync(
        ActivityRun run,
        DeployOptions options,
        JavaHomeLocatorDelegate javaHomeLocator)
    {
        logger.LogInformation("Begin deploy {}", run.Key);

        var profile = profileManager.GetImmutable(run.Key);
        var engine = new DeployEngine(run.Key,
                                      profile.Setup,
                                      provider,
                                      new() { FullCheckMode = options.FullCheckMode },
                                      LaunchDefinitionSnapshot.Load(run.Key),
                                      javaHomeLocator);

        var watch = Stopwatch.StartNew();
        foreach (var stage in engine)
        {
            run.Token.ThrowIfCancellationRequested();

            var current = StageOf(stage);
            run.Mutate<InstanceActivity.Deploying>(x => x with
            {
                CurrentStage = current,
                FileCount = null,
                Progress = new ActivityProgress.Indeterminate(current.ToString())
            });

            // 只有固实阶段能报出文件计数，其余阶段保持脉冲。
            if (stage is SolidifyManifestStage solidify)
            {
                solidify
                   .ProgressStream.Subscribe(x => run.Mutate<InstanceActivity.Deploying>(a => a with
                    {
                        FileCount = x,
                        Progress = new ActivityProgress.Determinate(current.ToString(),
                                                                    x.Total == 0 ? 0d : (double)x.Current / x.Total)
                    }))
                   .DisposeWith(solidify);
            }

            logger.LogInformation("Enter stage {name}", stage.GetType().Name);
            await stage.ProcessAsync(run.Token).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(1), run.Token).ConfigureAwait(false);
        }

        watch.Stop();
        logger.LogInformation("{key} deployed in {ms}ms", run.Key, watch.ElapsedMilliseconds);
    }

    private static DeployStage StageOf(StageBase stage) =>
        stage switch
        {
            LoadLockStage => DeployStage.LoadLock,
            ResolveComponentsStage => DeployStage.ResolveComponents,
            CompileLaunchStage => DeployStage.CompileLaunch,
            SyncPackagesStage => DeployStage.SyncPackages,
            FlattenPackagesStage => DeployStage.FlattenPackages,
            PersistLockStage => DeployStage.PersistLock,
            EnsureRuntimeStage => DeployStage.EnsureRuntime,
            GenerateManifestStage => DeployStage.GenerateManifest,
            SolidifyManifestStage => DeployStage.SolidifyManifest,
            _ => throw new NotSupportedException($"Unrecognized deploy stage {stage.GetType().Name}")
        };

    #endregion

    #region Launch

    public IObservable<InstanceActivity> Launch(
        string key,
        LaunchOptions options,
        JavaHomeLocatorDelegate javaHomeLocator)
    {
        var run = Register(new InstanceActivity.Running
        {
            Key = key,
            Id = Guid.NewGuid(),
            Options = options,
            Progress = new ActivityProgress.Indeterminate("Launching")
        });
        _ = ExecuteAsync(run, r => LaunchCoreAsync(r, options, javaHomeLocator));
        return run.Stream;
    }

    private async Task LaunchCoreAsync(
        ActivityRun run,
        LaunchOptions options,
        JavaHomeLocatorDelegate javaHomeLocator)
    {
        logger.LogInformation("Begin launch {}", run.Key);

        if (options.Account == null)
        {
            throw new InvalidOperationException("Account is not provided");
        }

        await ValidateAndRefreshAccountAsync(options, run.Token).ConfigureAwait(false);

        var profile = profileManager.GetImmutable(run.Key);

        var lockPath = PathDef.Default.FileOfLockData(run.Key);
        var found = File.Exists(lockPath);
        if (found)
        {
            if (JsonSerializer.Deserialize<LockData>(await File
                                                           .ReadAllTextAsync(lockPath, run.Token)
                                                           .ConfigureAwait(false),
                                                      JsonSerializerOptions.Web)
                  ?.Launch is not { } plan)
            {
                throw new InvalidOperationException("Lock has no compiled launch; deploy the instance first");
            }

            try
            {
                var java = await javaHomeLocator(plan.JavaMajors, run.Token).ConfigureAwait(false);
                if (java.Major != plan.Target.JavaMajor || java.Architecture != plan.Target.Architecture
                 || PlatformHelper.GetOsName() != plan.Target.Os || PlatformHelper.GetOsVersion() != plan.Target.OsVersion)
                {
                    throw new InvalidOperationException("The Java or platform target changed; deploy the instance again");
                }
                var javaHome = java.Home;
                var workingDir = PathDef.Default.DirectoryOfBuild(run.Key);
                var libraryDir = PathDef.Default.CacheLibraryDirectory;
                var assetDir = LaunchPathHelper.AssetDirectory(run.Key, plan);
                var nativeDir = PathDef.Default.DirectoryOfNatives(run.Key);
                var igniter = plan.MakeIgniter();

                run.Mutate<InstanceActivity.Running>(x => x with
                {
                    JavaHome = javaHome,
                    JavaVersion = java.Major
                });

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

                var launchContext = new AccountConfigurerAgent.LaunchContext(igniter, plan);
                await accountConfigurer
                     .ConfigureLaunchAsync(options.Account, launchContext, run.Token)
                     .ConfigureAwait(false);

                if (options.Mode == LaunchMode.Debug)
                {
                    igniter.Debug();
                }

                var process = igniter.Build();
                var build = PathDef.Default.DirectoryOfBuild(run.Key);
                if (!Directory.Exists(build))
                {
                    Directory.CreateDirectory(build);
                }

                var commandLine = FormatCommandLine(process.StartInfo);
                run.Mutate<InstanceActivity.Running>(x => x with { CommandLine = commandLine });

                if (options.Mode == LaunchMode.Debug)
                {
                    await File
                         .WriteAllLinesAsync(Path.Combine(build, "trident.launch.dump.txt"),
                                             [process.StartInfo.FileName, .. process.StartInfo.ArgumentList])
                         .ConfigureAwait(false);
                }

                if (options.Mode == LaunchMode.Managed)
                {
                    // 进程已起：运行中无「朝终点推进」的进度概念，改为运行指示。
                    run.Mutate<InstanceActivity.Running>(x => x with
                    {
                        Process = process,
                        Progress = new ActivityProgress.None()
                    });
                    var launcher = new LaunchEngine(process);
                    await foreach (var scrap in launcher.WithCancellation(run.Token).ConfigureAwait(false))
                    {
                        _scraps.OnNext(new(run.Key, scrap));
                    }

                    var detaching = run.Current is InstanceActivity.Running { IsDetaching: true };
                    run.Mutate<InstanceActivity.Running>(x => x with { Process = null });

                    if (run.Token.IsCancellationRequested)
                    {
                        if (!detaching)
                        {
                            process.Kill();
                        }
                    }
                    else
                    {
                        await process.WaitForExitAsync(run.Token).ConfigureAwait(false);

                        if (process.ExitCode != 0)
                        {
                            var code = process.ExitCode;
                            process.Close();
                            throw new ProcessFaultedException(code,
                                                              $"The process has exited with non-zero code {code}");
                        }
                    }

                    process.Close();
                    run.Token.ThrowIfCancellationRequested();
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
            throw new LockUnavailableException(run.Key, lockPath, found);
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

    public IObservable<InstanceActivity> Install(string key, string label, string? ns, string pid, string? vid)
    {
        // 仅在线安装有活动——离线导入无需等待，全在前端进行。

        var reserved = profileManager.RequestKey(key);
        try
        {
            var run = Register(new InstanceActivity.Installing { Key = reserved.Key, Id = Guid.NewGuid() });
            _ = ExecuteAsync(run, r => InstallCoreAsync(r, reserved, label, ns, pid, vid), reserved);
            return run.Stream;
        }
        catch
        {
            reserved.Dispose();
            throw;
        }
    }

    private async Task InstallCoreAsync(
        ActivityRun run,
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
        var imported = await DownloadAndImportPackageAsync(package, run, run.Token).ConfigureAwait(false);
        using var pack = imported.Pack;
        var container = imported.Container;
        await modpacks.InstallAsync(key, pack, container, run.Token).ConfigureAwait(false);

        var reference = container.Profile.Setup.Source;
        run.Mutate<InstanceActivity.Installing>(x => x with { Reference = reference });

        logger.LogInformation("{} added", key.Key);
    }

    #endregion

    #region Update

    public IObservable<InstanceActivity> Update(string key, string label, string? ns, string pid, string vid)
    {
        var run = Register(new InstanceActivity.Updating { Key = key, Id = Guid.NewGuid() });
        _ = ExecuteAsync(run, r => UpdateCoreAsync(r, key, label, ns, pid, vid));
        return run.Stream;
    }

    private async Task UpdateCoreAsync(
        ActivityRun run,
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
        var imported = await DownloadAndImportPackageAsync(package, run, run.Token).ConfigureAwait(false);
        using var pack = imported.Pack;
        var oldSource = profileManager.GetImmutable(key).Setup.Source;
        await modpacks.ApplyAsync(key, pack, imported.Container, run.Token).ConfigureAwait(false);
        run.Mutate<InstanceActivity.Updating>(x => x with
        {
            OldSource = oldSource, NewSource = imported.Container.Profile.Setup.Source
        });
        logger.LogInformation("{key} updated", key);
    }

    private async Task<(CompressedProfilePack Pack, ImportedProfileContainer Container)> DownloadAndImportPackageAsync(
        Package package,
        ActivityRun run,
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
        try
        {
            var container = await importers.ImportAsync(pack).ConfigureAwait(false);
            return (pack, container);
        }
        catch
        {
            pack.Dispose();
            throw;
        }
    }

    #endregion
}
