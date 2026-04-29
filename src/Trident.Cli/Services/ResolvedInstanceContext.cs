using Trident.Abstractions.FileModels;

namespace Trident.Cli.Services;

public sealed record ResolvedInstanceContext(
    string Key,
    string InstancePath,
    string ProfilePath,
    Profile Profile
);
