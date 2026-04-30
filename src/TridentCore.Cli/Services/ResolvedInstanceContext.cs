using TridentCore.Abstractions.FileModels;

namespace TridentCore.Cli.Services;

public sealed record ResolvedInstanceContext(
    string Key,
    string InstancePath,
    string ProfilePath,
    Profile Profile
);
