namespace TridentCore.Core.Engines.Deploying;

public enum DeployStage
{
    LoadLock,
    ResolveComponents,
    EnsureRuntime,
    CompileLaunch,
    SyncPackages,
    FlattenPackages,
    PersistLock,
    GenerateManifest,
    SolidifyManifest
}
