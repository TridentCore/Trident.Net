using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TridentCore.Abstractions;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Services;
using TridentCore.Tests.Utilities;
using TridentCore.Abstractions.Importers;
using TridentCore.Abstractions.Launching;
using TridentCore.Core.Importers;
using TridentCore.Core.Utilities;

namespace TridentCore.Tests;

[TestClass]
public sealed class MmcImportTests
{
    [TestMethod]
    public async Task ImportUsesListedComponentsAndIgnoresOrphanPatches()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            Add(zip, "pack/mmc-pack.json", """{"formatVersion":1,"components":[{"uid":"net.minecraft","version":"1.7.10"},{"uid":"custom","disabled":true}]}""");
            Add(zip, "pack/patches/net.minecraft.json", """{"uid":"net.minecraft","version":"1.7.10","mainClass":"custom.Main","compatibleJavaMajors":[17,21]}""");
            Add(zip, "pack/patches/orphan.json", "this is not valid JSON and must not be read");
            Add(zip, "pack/.minecraft/options.txt", "test");
        }
        stream.Position = 0;
        using var pack = new CompressedProfilePack(stream);
        var result = await new MultiMcImporter().ExtractAsync(pack);
        var definition = LaunchDefinitionHelper.Deserialize<LaunchDefinition>(Encoding.UTF8.GetString(
            result.GeneratedLaunchFiles!.Single(x => x.Target == "import/definition.json").Content), "fixture");
        Assert.AreEqual(2, definition.Components.Count);
        Assert.AreEqual(LaunchSelection.Binding.Minecraft, definition.Components[0].From);
        Assert.IsFalse(definition.Components[1].Enabled);
        Assert.IsTrue(result.GeneratedLaunchFiles!.Any(x => x.Target == "import/components/net.minecraft.json"));
        Assert.IsFalse(result.GeneratedLaunchFiles!.Any(x => x.Target.Contains("orphan")));
        Assert.AreEqual("options.txt", result.ImportFileNames.Single().Target);
    }

    [TestMethod]
    public async Task GtnhTemplatesFlowThroughImportResolutionAndCompilation()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            Add(zip, "mmc-pack.json", """
                {"formatVersion":1,"components":[
                  {"uid":"net.minecraft","version":"1.7.10"},
                  {"uid":"net.minecraftforge","version":"10.13.4.1614"},
                  {"uid":"org.lwjgl3","version":"fixture"}]}
                """);
            Add(zip, "patches/net.minecraft.json", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gtnh-minecraft.json")));
            Add(zip, "patches/net.minecraftforge.json", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gtnh-forge.json")));
            Add(zip, "patches/org.lwjgl3.json", """
                {"uid":"org.lwjgl3","version":"fixture","libraries":[{"name":"group:fixture:1:patches","MMC-hint":"local"}]}
                """);
            Add(zip, "libraries/fixture-1-patches.jar", "non-executable fixture");
        }
        stream.Position = 0;
        using var pack = new CompressedProfilePack(stream);
        var agent = new ImporterAgent([new MultiMcImporter()], NullLogger<ImporterAgent>.Instance);
        var imported = await agent.ImportAsync(pack);
        using var profiles = new ProfileManager(NullLogger<ProfileManager>.Instance);
        using var reserved = profiles.RequestKey(Guid.NewGuid().ToString("N"));
        var key = reserved.Key;
        try
        {
            var modpacks = new InstanceModpackService(profiles, agent, new FixtureHelper.OfflineHttpFactory(), NullLogger<InstanceModpackService>.Instance);
            await modpacks.InstallAsync(reserved, pack, imported, CancellationToken.None);
            var provider = new FixtureHelper.Provider();
            var resolution = await new LaunchDefinitionResolverService(provider).ResolveAsync(
                imported.Profile.Setup, LaunchDefinitionSnapshot.Load(key), CancellationToken.None);
            Assert.AreEqual(0, provider.Requests.Count);
            Assert.AreSequenceEqual(new[] { "net.minecraft", "net.minecraftforge", "org.lwjgl3" }, resolution.Components.Select(x => x.Definition.Id));
            var compiler = new LaunchCompilerService();
            foreach (var target in new[] { FixtureHelper.Target, FixtureHelper.Target with { Os = "windows", Architecture = "x64", JavaMajor = 21 } })
            {
                var result = compiler.Compile(resolution, target).Result;
                Assert.AreEqual("com.gtnewhorizons.retrofuturabootstrap.Main", result.MainClass);
                var patches = result.Artifacts.Single(x => x.Id.Namespace == "group");
                Assert.IsTrue(patches.Url.IsFile);
                Assert.IsTrue(File.Exists(ArtifactHelper.LocationOf(patches)));
                Assert.IsTrue(result.Classpath.Contains(patches.Id));
                Assert.IsTrue(result.Natives.Length > 0);
                var cached = JsonSerializer.Deserialize<CompiledLaunch>(JsonSerializer.Serialize(result))!;
                Assert.AreEqual(result.Fingerprint, cached.Fingerprint);
                Assert.AreSequenceEqual(result.Classpath, cached.Classpath);
                Assert.AreSequenceEqual(result.JavaArguments, cached.JavaArguments);
            }
        }
        finally
        {
            profiles.Remove(key);
            var home = PathDef.Default.DirectoryOfHome(key);
            if (Directory.Exists(home)) Directory.Delete(home, true);
        }
    }

    private static void Add(ZipArchive zip, string path, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path).Open());
        writer.Write(content);
    }
}
