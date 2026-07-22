namespace TridentCore.Pref;

public static class ProjectIdentifierExtensions
{
    public static ProjectIdentifier ToUnscoped(this ScopedProjectIdentifier self, string scope) =>
        new(scope, self.Namespace, self.Identity);

    public static ScopedProjectIdentifier ToScoped(this ProjectIdentifier self) => new(self.Namespace, self.Identity);

    public static PackageIdentifier ToPackageIdentifier(this ProjectIdentifier self, string? version = null) =>
        new(self.Repository, self.Namespace, self.Identity, version);
}
