namespace TridentCore.Core.Engines.Deploying;

public enum DeployStage
{
    CHECK_ARTIFACT,
    INSTALL_VANILLA,
    PROCESS_LOADER,
    RESOLVE_PACKAGE,
    BUILD_ARTIFACT,
    ENSURE_RUNTIME,
    GENERATE_MANIFEST,
    SOLIDIFY_MANIFEST,
}
