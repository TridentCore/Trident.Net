using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Launching;
using TridentCore.Core.Services.Instances;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Engines.Deploying;

public class DeployContext(
    string key,
    Profile.Rice setup,
    IServiceProvider provider,
    DeployEngineOptions options,
    LaunchDefinitionSnapshot definitions,
    JavaHomeLocatorDelegate javaHomeLocator)
{
    internal LockData? BaseLock;
    internal LockData Lock = null!;
    internal LaunchResolution Resolution = null!;
    internal JavaHelper.JavaResolution Java = null!;
    internal EntityManifest? Manifest;
    internal BundledRuntime? Runtime;

    public string Key => key;
    public Profile.Rice Setup => setup;
    public IServiceProvider Provider => provider;
    public DeployEngineOptions Options => options;
    public LaunchDefinitionSnapshot Definitions => definitions;
    public JavaHomeLocatorDelegate JavaHomeLocator => javaHomeLocator;
}
