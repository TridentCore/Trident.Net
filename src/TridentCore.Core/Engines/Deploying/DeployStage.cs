namespace TridentCore.Core.Engines.Deploying;

public enum DeployStage
{
    LoadLock,
    InstallVanilla,
    ProcessLoader,
    SyncPackages,
    FlattenPackages,
    PersistLock,
    EnsureRuntime,
    GenerateManifest,
    SolidifyManifest
}
