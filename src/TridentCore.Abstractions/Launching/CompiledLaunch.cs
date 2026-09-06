using System.Collections.Immutable;

namespace TridentCore.Abstractions.Launching;

public sealed record CompiledLaunch(
    string Fingerprint,
    LaunchTarget Target,
    ImmutableArray<uint> JavaMajors,
    string MainClass,
    LaunchAssetIndex AssetIndex,
    ImmutableArray<string> GameArguments,
    ImmutableArray<string> JavaArguments,
    ImmutableArray<LaunchArtifact> Artifacts,
    ImmutableArray<LaunchArtifact.Identity> Classpath,
    ImmutableArray<CompiledLaunch.NativeExtraction> Natives)
{
    public sealed record NativeExtraction(LaunchArtifact.Identity Artifact, ImmutableArray<string> Excludes);
}
