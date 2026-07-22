using TridentCore.Abstractions.Repositories.Resources;

namespace TridentCore.Core.Utilities;

// The single source of truth for a package's in-build relative target path. Consumed by
// FlattenPackages (conflict grouping), GenerateManifest (materialization), and PackagePlanner
// (standalone planning/export) so the three sites can never drift apart.
public static class PackagePathHelper
{
    public static string RelativeTarget(
        bool normalizing,
        string? destination,
        string projectName,
        string fileName,
        ResourceKind kind)
    {
        var actual = normalizing
                         ? string.Concat(FileHelper.Sanitize(projectName), Path.GetExtension(fileName))
                         : fileName;
        return destination is not null
                   ? Path.Combine(destination, actual)
                   : Path.Combine(FileHelper.GetAssetFolderName(kind), actual);
    }
}
