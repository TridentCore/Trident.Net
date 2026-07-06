namespace TridentCore.Core.Engines.Deploying;

public enum DeployStage
{
    LoadLock,
    InstallVanilla,
    ProcessLoader,
    ResolvePackage,
    PersistLock,
    EnsureRuntime,
    GenerateManifest,
    SolidifyManifest,
}
