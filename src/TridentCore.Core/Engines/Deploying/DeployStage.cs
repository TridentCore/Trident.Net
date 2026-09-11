namespace TridentCore.Core.Engines.Deploying;

public enum DeployStage
{
    LoadLock,
    InstallVanilla,
    ProcessLoader,
    ApplyLaunchPatch,
    SyncPackages,
    FlattenPackages,
    PersistLock,
    EnsureRuntime,
    GenerateManifest,
    SolidifyManifest
}
