namespace TridentCore.Core.Engines.Deploying;

public enum DeployStage
{
    LoadLock,
    InstallVanilla,
    ProcessLoader,
    ApplyLaunchPatch,
    SyncPackages,
    PersistLock,
    SelectRuntime,
    GenerateManifest,
    SolidifyManifest
}
