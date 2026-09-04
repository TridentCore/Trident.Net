namespace TridentCore.Core.Engines.Deploying;

public enum DeployStage
{
    LoadLock,
    LoadLaunchPlan,
    InstallVanilla,
    ProcessLoader,
    ResolveLaunchPlan,
    SyncPackages,
    FlattenPackages,
    EnsureRuntime,
    PersistLock,
    GenerateManifest,
    SolidifyManifest
}
