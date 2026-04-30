using TridentCore.Purl.Building;

namespace TridentCore.Purl;

public static class PackageDescriptorExtensions
{
    public static string Build(this PackageDescriptor self) =>
        Builder.Build(
            self.Repository,
            self.Namespace,
            self.Identity,
            self.Version,
            self.Filters.AsSpan()
        );
}
