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
    // BaseLock: read-only snapshot of the on-disk lock (null when absent or legacy FORMAT=1).
    // Lock: the product being built this cycle. Stages judge validity against BaseLock and
    // migrate/rebuild into Lock.
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
