namespace TridentCore.Pref;

public static class PackageIdentifierExtensions
{
    public static PackageIdentifier ToUnscoped(this ScopedPackageIdentifier self, string scope) =>
        new(scope, self.Namespace, self.Identity, self.Version);

    public static ScopedPackageIdentifier ToScoped(this PackageIdentifier self) =>
        new(self.Namespace, self.Identity, self.Version);

    public static ProjectIdentifier ToProjectIdentifier(this PackageIdentifier self) =>
        new(self.Repository, self.Namespace, self.Identity);
}
