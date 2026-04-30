namespace TridentCore.Purl;

public static class PackageIdentifierExtensions
{
    public static PackageIdentifier ToUnscoped(this ScopedPackageIdentifier self, string scope) =>
        new(scope, self.Namespace, self.Identity, self.Version);

    public static ScopedPackageIdentifier ToScoped(this PackageIdentifier self) =>
        new(self.Namespace, self.Identity, self.Version);
}
