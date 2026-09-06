using TridentCore.Abstractions.Utilities;

namespace TridentCore.Abstractions.Launching;

public sealed record LaunchArtifact(LaunchArtifact.Identity Id, Uri Url, FileHash? Hash = null)
{
    public sealed record Identity(string Namespace, string Name, string Version, string? Classifier = null, string Extension = "jar");
}
