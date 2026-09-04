using System.Collections.Immutable;

namespace TridentCore.Abstractions.FileModels;

public sealed record LaunchPlanResult(
    string MainClass,
    uint JavaMajorVersion,
    ImmutableArray<string> GameArguments,
    ImmutableArray<string> JavaArguments,
    ImmutableArray<LockData.Library> Libraries,
    LockData.AssetData AssetIndex);
