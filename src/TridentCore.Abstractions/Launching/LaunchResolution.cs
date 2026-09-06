using System.Collections.Immutable;

namespace TridentCore.Abstractions.Launching;

public sealed record LaunchResolution(
    ImmutableArray<ResolvedLaunchComponent> Components,
    ImmutableArray<uint> JavaMajors,
    string Fingerprint);
