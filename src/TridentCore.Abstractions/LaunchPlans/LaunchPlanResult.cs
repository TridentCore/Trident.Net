using System.Collections.Immutable;
using TridentCore.Abstractions.FileModels;

namespace TridentCore.Abstractions.LaunchPlans;

public sealed record LaunchPlanResult(
    string MainClass,
    uint JavaMajorVersion,
    ImmutableArray<string> GameArguments,
    ImmutableArray<string> JavaArguments,
    ImmutableArray<LockData.Library> Libraries,
    LockData.AssetData AssetIndex);
