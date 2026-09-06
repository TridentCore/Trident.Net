using Microsoft.VisualStudio.TestTools.UnitTesting;
using TridentCore.Abstractions;
using TridentCore.Abstractions.Utilities;

namespace TridentCore.Tests;

[TestClass]
public sealed class ArtifactTests
{
    private static readonly string _home = Path.Combine(AppContext.BaseDirectory, "home-" + Guid.NewGuid().ToString("N"));
    private static readonly string? _previousHome = Environment.GetEnvironmentVariable("TRIDENT_HOME");

    [AssemblyInitialize]
    public static void Initialize(TestContext _)
    {
        Environment.SetEnvironmentVariable("TRIDENT_HOME", _home);
        PathDef.BrandNames = PathDef.TridentNames;
        Assert.IsTrue(PathDef.Default.InstanceDirectory.StartsWith(_home + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Directory.CreateDirectory(_home);
    }

    [AssemblyCleanup]
    public static void Cleanup()
    {
        if (Directory.Exists(_home)) Directory.Delete(_home, true);
        Environment.SetEnvironmentVariable("TRIDENT_HOME", _previousHome);
    }

    [TestMethod]
    public void MavenVersionAllowsPlus()
    {
        var id = ArtifactHelper.Parse("net.fabricmc:sponge-mixin:0.15.5+mixin.0.8.7");
        Assert.AreEqual("0.15.5+mixin.0.8.7", id.Version);
        var uri = ArtifactHelper.ResolveRepository(new("https://maven.example/repository"), id);
        Assert.IsTrue(uri.AbsoluteUri.Contains("/repository/net/fabricmc/sponge-mixin/"));
        Assert.IsTrue(Uri.UnescapeDataString(uri.AbsoluteUri).EndsWith("sponge-mixin-0.15.5+mixin.0.8.7.jar"));
    }
}
