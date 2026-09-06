using Microsoft.VisualStudio.TestTools.UnitTesting;
using TridentCore.Abstractions.Launching;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;
using TridentCore.Tests.Utilities;

namespace TridentCore.Tests;

[TestClass]
public sealed class DefinitionStorageTests
{
    [TestMethod]
    public void ImportedLocalReferencesCannotEscapeTheirLayer()
    {
        using var sandbox = FixtureHelper.CreateSandbox();
        sandbox.Write("user/private.jar", [1, 2, 3]);
        sandbox.WriteJson("import/components/game.json", FixtureHelper.Game() with
        {
            Libraries = [new(FixtureHelper.Artifact("group:library:1") with { Url = new("../../user/private.jar", UriKind.Relative) })]
        });
        var snapshot = LaunchDefinitionSnapshot.LoadDirectory(sandbox.Root);
        Assert.ThrowsExactly<FormatException>(() => snapshot.Resolve(snapshot.Components["game"]));
    }

    [TestMethod]
    public async Task ExternalUserArtifactChangesInvalidateResolution()
    {
        using var sandbox = FixtureHelper.CreateSandbox();
        sandbox.Write("external.jar", [1]);
        sandbox.WriteJson("user/definition.json", new LaunchDefinition { Components = [new() { Id = "game", Version = "1" }] });
        sandbox.WriteJson("user/components/game.json", FixtureHelper.Game() with
        {
            Libraries = [new(FixtureHelper.Artifact("group:library:1") with { Url = new("../../external.jar", UriKind.Relative) })]
        });
        var resolver = new LaunchDefinitionResolverService(new FixtureHelper.Provider());
        var first = await resolver.ResolveAsync(new() { Version = "1" }, LaunchDefinitionSnapshot.LoadDirectory(sandbox.Root), CancellationToken.None);
        sandbox.Write("external.jar", [2]);
        var second = await resolver.ResolveAsync(new() { Version = "1" }, LaunchDefinitionSnapshot.LoadDirectory(sandbox.Root), CancellationToken.None);
        Assert.AreNotEqual(first.Fingerprint, second.Fingerprint);
    }
}
