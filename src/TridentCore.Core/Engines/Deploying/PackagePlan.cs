namespace TridentCore.Core.Engines.Deploying;

public record PackagePlan(
    string Label,
    string? Namespace,
    string ProjectId,
    string VersionId,
    string RelativeTargetPath,
    Uri Url,
    string? Sha1
)
{ }
