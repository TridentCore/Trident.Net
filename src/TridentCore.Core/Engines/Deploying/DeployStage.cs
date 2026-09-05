namespace TridentCore.Core.Engines.Deploying;

public enum DeployStage
{
    LoadLock,
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
