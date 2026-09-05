using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.LaunchPlans;

namespace TridentCore.Abstractions.Importers;

public record ImportedProfileContainer(
    Profile Profile,
    IReadOnlyList<(string Source, string Target)> ImportFileNames,
    IReadOnlyList<(string Source, string Target)> HomeFileNames,
    Uri? IconUrl,
    IReadOnlyList<(string Source, string Target)> LaunchFileNames,
    IReadOnlyList<(string Target, byte[] Content)>? GeneratedLaunchFiles = null,
    IReadOnlyList<LaunchPlanDiagnostic>? LaunchPlanDiagnostics = null,
    bool ReplacesManagedLaunchSource = false);
