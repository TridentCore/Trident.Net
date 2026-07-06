using TridentCore.Abstractions.Utilities;

namespace TridentCore.Core.Engines.Deploying;

public record PackagePlan(
    string Label,
    string? Namespace,
    string ProjectId,
    string VersionId,
    string RelativeTargetPath,
    Uri Url,
    FileHash? Hash)
{
    public bool IsSkipping { get; init; }
}
