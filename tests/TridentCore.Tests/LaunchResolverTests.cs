using Microsoft.VisualStudio.TestTools.UnitTesting;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Launching;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;
using TridentCore.Tests.Utilities;

namespace TridentCore.Tests;

[TestClass]
public sealed class LaunchResolverTests
{
    private static readonly Profile.Rice Setup = new() { Version = "1" };

    [TestMethod]
    public async Task LocalComponentReplacesRemoteBeforeDependenciesAreRead()
    {
        using var sandbox = FixtureHelper.CreateSandbox();
        sandbox.WriteJson("import/definition.json", new LaunchDefinition { Components = [new() { Id = "game", Version = "1" }] });
        sandbox.WriteJson("import/components/game.json", FixtureHelper.Game());
        var provider = new FixtureHelper.Provider(FixtureHelper.Game() with { Requires = [new("obsolete", "1")] });
        var resolution = await new LaunchDefinitionResolverService(provider).ResolveAsync(Setup,
            LaunchDefinitionSnapshot.LoadDirectory(sandbox.Root), CancellationToken.None);
        Assert.AreEqual(0, provider.Requests.Count);
        Assert.AreEqual(1, resolution.Components.Length);
    }

    [TestMethod]
    public async Task ExplicitOrderIsNotTopologicallyRewritten()
    {
        var a = FixtureHelper.Game("a") with { Requires = [new("b", "1")] };
        var b = new LaunchComponent { Id = "b", Version = "1" };
        var snapshot = new LaunchDefinitionSnapshot(new() { Components = [new() { Id = "a", Version = "1" }, new() { Id = "b", Version = "1" }] },
            new Dictionary<string, LaunchDefinitionSnapshot.Entry>(), "fixture");
        var resolution = await new LaunchDefinitionResolverService(new FixtureHelper.Provider(a, b))
            .ResolveAsync(Setup, snapshot, CancellationToken.None);
        Assert.AreSequenceEqual(new[] { "a", "b" }, resolution.Components.Select(x => x.Definition.Id));
    }

    [TestMethod]
    public async Task UserDefinitionWinsWithoutMergingImportedBody()
    {
        using var sandbox = FixtureHelper.CreateSandbox();
        sandbox.WriteJson("import/definition.json", new LaunchDefinition { Components = [new() { Id = "game", Version = "1" }] });
        sandbox.WriteJson("import/components/game.json", FixtureHelper.Game() with { JavaArguments = new() { Append = [new("-Dimport=true")] } });
        sandbox.WriteJson("user/components/game.json", FixtureHelper.Game() with { MainClass = "user.Main" });
        sandbox.WriteJson("user/components/inactive.json", new LaunchComponent { Id = "inactive", MainClass = "wrong.Main" });
        var snapshot = LaunchDefinitionSnapshot.LoadDirectory(sandbox.Root);
        var resolution = await new LaunchDefinitionResolverService(new FixtureHelper.Provider()).ResolveAsync(Setup, snapshot, CancellationToken.None);
        Assert.AreEqual("user.Main", resolution.Components.Single().Definition.MainClass);
        Assert.AreEqual(0, resolution.Components.Single().Definition.JavaArguments.Append.Count);
        var exported = LaunchDefinitionSnapshot.LoadDirectory(sandbox.Root, includeUser: false);
        Assert.AreEqual("game.Main", exported.Components["game"].Definition.MainClass);
        Assert.IsFalse(exported.Components.ContainsKey("inactive"));
    }

    [TestMethod]
    public async Task JavaRequirementsAreIntersectedInsteadOfSelectingFirstValue()
    {
        var a = FixtureHelper.Game("a");
        var b = new LaunchComponent { Id = "b", Version = "1", JavaMajors = [21u, 25u] };
        var snapshot = new LaunchDefinitionSnapshot(new() { Components = [new() { Id = "a", Version = "1" }, new() { Id = "b", Version = "1" }] },
            new Dictionary<string, LaunchDefinitionSnapshot.Entry>(), "fixture");
        var resolution = await new LaunchDefinitionResolverService(new FixtureHelper.Provider(a, b))
            .ResolveAsync(Setup, snapshot, CancellationToken.None);
        Assert.AreSequenceEqual(new[] { 21u }, resolution.JavaMajors);
    }
}
