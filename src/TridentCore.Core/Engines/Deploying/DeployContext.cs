using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.LaunchPlans;
using TridentCore.Core.Services.Instances;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Engines.Deploying;

public class DeployContext(
    string key,
    Profile.Rice setup,
    IServiceProvider provider,
    DeployEngineOptions options,
    LaunchPlanSnapshot? launchPlanSnapshot,
    JavaHomeLocatorDelegate javaHomeLocator)
{
    // BaseLock 是磁盘锁的只读快照；Lock 是本周期重新生成的状态。
    // 阶段对照 BaseLock 判有效性并迁移/重建进 Lock。
    internal LockData? BaseLock;
    internal LockData Lock = null!;
    internal LaunchPlan? LaunchPlan;
    internal LaunchPlanDocument? LaunchPlanDocument;
    internal EntityManifest? Manifest;
    internal BundledRuntime? Runtime;

    public string Key => key;

    public Profile.Rice Setup => setup;
    public IServiceProvider Provider => provider;
    public DeployEngineOptions Options => options;
    public LaunchPlanSnapshot? LaunchPlanSnapshot => launchPlanSnapshot;
    public string? LaunchPlanHash => launchPlanSnapshot?.Hash;
    public JavaHomeLocatorDelegate JavaHomeLocator => javaHomeLocator;

    internal bool CanReuseLaunchPlan => BaseLock?.Platform == Lock.Platform
                                      && BaseLock.LaunchPlan is not null
                                      && BaseLock.Viability.LaunchPlanHash == LaunchPlanHash;
}
