using TridentCore.Abstractions.FileModels;

namespace TridentCore.Abstractions.Importers;

public record ImportedProfileContainer(
    Profile Profile,
    IReadOnlyList<(string Source, string Target)> ImportFileNames,
    IReadOnlyList<(string Source, string Target)> HomeFileNames,
    Uri? IconUrl
);
