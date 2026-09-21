using TridentCore.Abstractions.Utilities;

namespace TridentCore.Core.Engines.Deploying;

public sealed record DeploymentFileRequirement(
    string Path,
    Uri? Url,
    FileHash? Hash,
    bool Executable = false);
