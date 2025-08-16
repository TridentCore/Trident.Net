using Trident.Purl.Building;
using Trident.Purl.Parsing;

namespace Trident.Purl
{
    public static class PackageDescriptorExtensions
    {
        public static string Build(this PackageDescriptor self) =>
            Builder.Build(self.Repository, self.Identity, self.Namespace, self.Version, self.Filters.AsSpan());
    }
}