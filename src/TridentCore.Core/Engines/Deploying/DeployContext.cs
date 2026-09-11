using TridentCore.Abstractions.FileModels;

namespace TridentCore.Core.Engines.Deploying;

public class DeployContext(
    string key,
    Profile.Rice setup,
    IServiceProvider provider,
    DeployEngineOptions options,
    IReadOnlyList<(uint? Major, string Home)> javaVault)
{
    // BaseLock 是磁盘锁的只读快照（缺失或不可读时为 null）；Lock 是本周期的产物。
    // 阶段各自对照 BaseLock 中对应的 region 记录判有效性并迁移/重建进 Lock。
    internal LockData? BaseLock;
    internal LockData Lock = null!;
    internal PatchSet Patches = null!;
    internal EntityManifest? Manifest;
    internal BundledRuntime? Runtime;

    public string Key => key;

    public Profile.Rice Setup => setup;
    public IServiceProvider Provider => provider;
    public DeployEngineOptions Options => options;
    public IReadOnlyList<(uint? Major, string Home)> Vault => javaVault;
}
