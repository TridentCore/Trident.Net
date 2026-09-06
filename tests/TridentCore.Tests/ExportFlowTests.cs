using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TridentCore.Abstractions;
using TridentCore.Abstractions.Exporters;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Importers;
using TridentCore.Abstractions.Launching;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Engines.Deploying;
using TridentCore.Core.Exporters;
using TridentCore.Core.Importers;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;
using TridentCore.Tests.Utilities;

namespace TridentCore.Tests;

[TestClass]
public sealed class ExportFlowTests
{
    [TestMethod]
    public async Task MmcArchiveRoundTripCarriesImportedFilesButNotUserDefinitions()
    {
        using var profiles = new ProfileManager(NullLogger<ProfileManager>.Instance);
        using var reserved = profiles.RequestKey(Guid.NewGuid().ToString("N"));
        var key = reserved.Key;
        var restoredKey = Guid.NewGuid().ToString("N");
        profiles.Add(reserved, new Profile { Name = "Pack", Setup = new() { Version = "1" } });
        try
        {
            var launch = PathDef.Default.DirectoryOfLaunch(key);
            Write("import/definition.json", LaunchDefinitionHelper.Serialize(new LaunchDefinition
                { Components = [new() { From = LaunchSelection.Binding.Minecraft }] }));
            var artifact = new LaunchArtifact(ArtifactHelper.Parse("group:local:1"), new Uri("../files/local.jar", UriKind.Relative));
            Write("import/components/net.minecraft.json", LaunchDefinitionHelper.Serialize(FixtureHelper.Game("net.minecraft") with
            {
                MainClass = "pack.Main", Libraries = [new(artifact)],
                JavaArguments = new() { Append = [new("-Dpack=true")] }
            }));
            Write("import/files/local.jar", "local library fixture"u8.ToArray());
            Write("user/components/broken.json", "not part of the export"u8.ToArray());
            var provider = new FixtureHelper.Provider();
            using var services = new ServiceCollection()
                .AddSingleton(new PackagePlanner(NullLogger<PackagePlanner>.Instance, null!))
                .AddSingleton(new PackageMaterializer(NullLogger<PackageMaterializer>.Instance, new OfflineFactory()))
                .BuildServiceProvider();
            var exporter = new MultiMcExporter(new LaunchDefinitionResolverService(provider), services);
            var agent = new ExporterAgent([exporter], profiles, NullLogger<ExporterAgent>.Instance);
            using var container = await agent.ExportAsync(new PackData(), exporter.Label, key, "Pack", "Author", "1");
            using var stream = new MemoryStream();
            await agent.PackCompressedAsync(stream, container);
            stream.Position = 0;
            using var pack = new CompressedProfilePack(stream);
            Assert.IsFalse(pack.FileNames.Any(x => x.Contains("user") || x.Contains("broken")));
            Assert.IsTrue(pack.FileNames.Contains("libraries/local-1.jar"));
            var importer = new ImporterAgent([new MultiMcImporter()], NullLogger<ImporterAgent>.Instance);
            var imported = await importer.ImportAsync(pack);
            using var restored = profiles.RequestKey(restoredKey);
            var modpacks = new InstanceModpackService(profiles, importer, new OfflineFactory(), NullLogger<InstanceModpackService>.Instance);
            await modpacks.InstallAsync(restored, pack, imported, CancellationToken.None);
            var resolution = await new LaunchDefinitionResolverService(provider).ResolveAsync(imported.Profile.Setup,
                LaunchDefinitionSnapshot.Load(restoredKey), CancellationToken.None);
            var compiled = new LaunchCompilerService().Compile(resolution, FixtureHelper.Target).Result;
            Assert.AreEqual("pack.Main", compiled.MainClass);
            Assert.IsTrue(compiled.JavaArguments.Contains("-Dpack=true"));
            Assert.AreEqual("local library fixture", File.ReadAllText(ArtifactHelper.LocationOf(compiled.Artifacts.Single())));
            Assert.AreEqual(0, provider.Requests.Count);

            void Write(string relative, byte[] bytes)
            {
                var path = Path.Combine(launch, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }
        }
        finally
        {
            profiles.Remove(key);
            profiles.Remove(restoredKey);
            foreach (var value in new[] { key, restoredKey })
            {
                var home = PathDef.Default.DirectoryOfHome(value);
                if (Directory.Exists(home)) Directory.Delete(home, true);
            }
        }
    }

    [TestMethod]
    public async Task IncapableExporterRejectsImportedDefinitionsButIgnoresUserOnlyFiles()
    {
        using var profiles = new ProfileManager(NullLogger<ProfileManager>.Instance);
        using var reserved = profiles.RequestKey(Guid.NewGuid().ToString("N"));
        var key = reserved.Key;
        profiles.Add(reserved, new Profile { Name = "Pack", Setup = new() { Version = "1" } });
        var exporter = new PlainExporter();
        var agent = new ExporterAgent([exporter], profiles, NullLogger<ExporterAgent>.Instance);
        var launch = PathDef.Default.DirectoryOfLaunch(key);
        try
        {
            Directory.CreateDirectory(Path.Combine(launch, "import"));
            File.WriteAllBytes(Path.Combine(launch, "import", "definition.json"), LaunchDefinitionHelper.Serialize(new LaunchDefinition { Components = [] }));
            await Assert.ThrowsExactlyAsync<NotSupportedException>(() => agent.ExportAsync(new(), exporter.Label, key, "Pack", "Author", "1"));
            Assert.AreEqual(0, exporter.Calls);
            Directory.Move(Path.Combine(launch, "import"), Path.Combine(launch, "user"));
            using var result = await agent.ExportAsync(new(), exporter.Label, key, "Pack", "Author", "1");
            Assert.AreEqual(1, exporter.Calls);
        }
        finally
        {
            profiles.Remove(key);
            Directory.Delete(PathDef.Default.DirectoryOfHome(key), true);
        }
    }

    private sealed class PlainExporter : IProfileExporter
    {
        public string Label => "fixture";
        public bool SupportsLaunchDefinitions => false;
        public int Calls { get; private set; }
        public Task<PackedProfileContainer> PackAsync(UncompressedProfilePack pack)
        {
            Calls++;
            return Task.FromResult(new PackedProfileContainer(pack.Key) { OverrideDirectoryName = "import" });
        }
    }

    private sealed class OfflineFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Unexpected HTTP request");
    }
}
