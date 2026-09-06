using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TridentCore.Abstractions.Exporters;
using TridentCore.Abstractions.Launching;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Models.PrismLauncherApi;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;
using TridentCore.Tests.Utilities;

namespace TridentCore.Tests;

[TestClass]
public sealed class MetadataTests
{
    [TestMethod]
    public void FabricFixtureAcceptsMixinVersion()
    {
        var converted = MetadataComponentHelper.Convert(FixtureHelper.Metadata("fabric-0.16.14.json"),
            "net.fabricmc.fabric-loader", "0.16.14", "1.21.1");
        Assert.IsTrue(converted.Component.Libraries.Any(x => x.Artifact.Id.Version.Contains('+')));
    }

    [TestMethod]
    public void ForgeFixtureKeepsAllInstallerAsmVersions()
    {
        var converted = MetadataComponentHelper.Convert(FixtureHelper.Metadata("forge-47.4.0.json"),
            "net.minecraftforge", "47.4.0", "1.20.1");
        var game = FixtureHelper.Game() with
        {
            Libraries = [new(FixtureHelper.Artifact("com.mojang:minecraft:1.20.1:client"), LaunchLibrary.Usage.Client)]
        };
        var result = new LaunchCompilerService().Compile(FixtureHelper.Resolution(game, converted.Component), FixtureHelper.Target).Result;
        var versions = result.Artifacts.Where(x => x.Id.Namespace == "org.ow2.asm" && x.Id.Name == "asm").Select(x => x.Id.Version);
        Assert.IsTrue(versions.Contains("9.2"));
        Assert.IsTrue(versions.Contains("9.6"));
        Assert.IsTrue(versions.Contains("9.7.1"));
        Assert.AreEqual("9.7.1", result.Classpath.Single(x => x.Namespace == "org.ow2.asm" && x.Name == "asm").Version);
    }

    [TestMethod]
    public void GtnhNativesUseClassifiersAndHonorTargetRules()
    {
        var converted = MetadataComponentHelper.Convert(FixtureHelper.Metadata("gtnh-minecraft.json"), "net.minecraft", "1.7.10", "1.7.10");
        var compiler = new LaunchCompilerService();
        var resolution = FixtureHelper.Resolution(converted.Component);
        var mac = compiler.Compile(resolution, FixtureHelper.Target).Result;
        Assert.IsTrue(mac.Artifacts.Any(x => x.Id.Name == "jinput-platform" && x.Id.Classifier == "natives-osx-arm64"));
        Assert.IsFalse(mac.Artifacts.Any(x => x.Id.Name == "twitch-external-platform"));
        Assert.IsFalse(mac.Artifacts.Any(x => x.Id.Name == "twitch-platform" && x.Id.Classifier is null));
        var linux = compiler.Compile(resolution, FixtureHelper.Target with { Os = "linux", Architecture = "x64" }).Result;
        Assert.IsFalse(linux.Artifacts.Any(x => x.Id.Namespace == "tv.twitch" && x.Id.Name.EndsWith("platform")));
        var windows = compiler.Compile(resolution, FixtureHelper.Target with { Os = "windows", Architecture = "x64" }).Result;
        Assert.IsTrue(windows.Artifacts.Any(x => x.Id.Name == "twitch-platform" && x.Id.Classifier == "natives-windows-64"));
    }

    [TestMethod]
    public void NativeMetadataExportRoundTripsTargetSelection()
    {
        var converted = MetadataComponentHelper.Convert(FixtureHelper.Metadata("gtnh-minecraft.json"), "net.minecraft", "1.7.10", "1.7.10").Component;
        using var container = new PackedProfileContainer("fixture") { OverrideDirectoryName = ".minecraft" };
        var patch = MetadataExportHelper.Convert(converted, converted.GameArguments.Replace!, container);
        var imported = MetadataComponentHelper.Convert(patch.Deserialize<Component>(JsonSerializerOptions.Web)!, "net.minecraft", "1.7.10", "1.7.10").Component;
        var compiler = new LaunchCompilerService();
        foreach (var target in new[] { FixtureHelper.Target, FixtureHelper.Target with { Os = "windows", Architecture = "x64" },
                     FixtureHelper.Target with { Os = "linux", Architecture = "arm64" } })
        {
            var expected = compiler.Compile(FixtureHelper.Resolution(converted), target).Result;
            var actual = compiler.Compile(FixtureHelper.Resolution(imported), target).Result;
            Assert.AreSequenceEqual(expected.Artifacts.Select(x => ArtifactHelper.CoordinateOf(x.Id)).Order(),
                                     actual.Artifacts.Select(x => ArtifactHelper.CoordinateOf(x.Id)).Order());
            Assert.AreSequenceEqual(expected.Natives.Select(x => JsonSerializer.Serialize(x)),
                                     actual.Natives.Select(x => JsonSerializer.Serialize(x)));
        }
    }

    [TestMethod]
    public void NativeExportPreservesInterleavedVersionPrioritiesPerPlatform()
    {
        var source = FixtureHelper.Game() with
        {
            Libraries = [.. Natives("1", "osx"), .. Natives("2", "osx"), .. Natives("2", "linux"), .. Natives("1", "linux")]
        };
        using var container = new PackedProfileContainer("fixture") { OverrideDirectoryName = ".minecraft" };
        var patch = MetadataExportHelper.Convert(source, source.GameArguments.Replace!, container);
        var imported = MetadataComponentHelper.Convert(patch.Deserialize<Component>(JsonSerializerOptions.Web)!, "game", "1", "1").Component;
        var compiler = new LaunchCompilerService();
        foreach (var os in new[] { "osx", "linux" })
        {
            var target = FixtureHelper.Target with { Os = os };
            var expected = compiler.Compile(FixtureHelper.Resolution(source), target).Result;
            var actual = compiler.Compile(FixtureHelper.Resolution(imported), target).Result;
            Assert.AreEqual(os == "osx" ? "2" : "1", actual.Natives.Single().Artifact.Version);
            Assert.AreSequenceEqual(expected.Natives.Select(x => JsonSerializer.Serialize(x)), actual.Natives.Select(x => JsonSerializer.Serialize(x)));
        }

        static IEnumerable<LaunchLibrary> Natives(string version, string os) =>
            new[] { "x86", "x64", "arm", "arm64" }.Select(architecture =>
                new LaunchLibrary(FixtureHelper.Artifact($"group:native:{version}:native"), LaunchLibrary.Usage.Native)
                { Platform = new(os, architecture) });
    }
}
