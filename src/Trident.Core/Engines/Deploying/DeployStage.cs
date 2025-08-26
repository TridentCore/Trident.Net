namespace Trident.Core.Engines.Deploying
{
    public enum DeployStage
    {
        CheckArtifact,
        InstallVanilla,
        ProcessLoader,
        ResolvePackage,
        BuildArtifact,
        EnsureRuntime,
        GenerateManifest,
        SolidifyManifest
    }
}
