using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Launching;

namespace TridentCore.Abstractions.Importers;

public record ImportedProfileContainer(
    Profile Profile,
    IReadOnlyList<(string Source, string Target)> ImportFileNames,
    IReadOnlyList<(string Source, string Target)> HomeFileNames,
    Uri? IconUrl,
    IReadOnlyList<(string Source, string Target)> LaunchFileNames,
    IReadOnlyList<(string Target, byte[] Content)>? GeneratedLaunchFiles = null,
    IReadOnlyList<LaunchDiagnostic>? LaunchDiagnostics = null,
    bool ReplacesLaunchImport = false);
