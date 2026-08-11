using TridentCore.Abstractions.FileModels;
using TridentCore.Core.Services.Instances;

namespace TridentCore.Core.Engines.Deploying;

public class DeployContext(
    string key,
    Profile.Rice setup,
    IServiceProvider provider,
    DeployEngineOptions options,
    string optionsHash,
    string priorityHash,
    JavaHomeLocatorDelegate javaHomeLocator)
{
    // NOTE: BaseLock 是磁盘锁的只读快照（缺失或旧 FORMAT=1 时为 null）；Lock 是本周期的产物。
    //  阶段对照 BaseLock 判有效性并迁移/重建进 Lock。
    internal LockData? BaseLock;
    internal LockData Lock = null!;
    internal EntityManifest? Manifest;
    internal BundledRuntime? Runtime;

    public string Key => key;

    public Profile.Rice Setup => setup;
    public IServiceProvider Provider => provider;
    public DeployEngineOptions Options => options;
    public string OptionsHash => optionsHash;
    public string PriorityHash => priorityHash;
    public JavaHomeLocatorDelegate JavaHomeLocator => javaHomeLocator;
}
