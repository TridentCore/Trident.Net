using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Repositories;

namespace TridentCore.Core.Engines.Deploying;

public record PackagePlannerContext(IReadOnlyList<Profile.Rice.Rule> Rules, Filter Filter) { }
