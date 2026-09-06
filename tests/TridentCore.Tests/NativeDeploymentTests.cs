using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Launching;
using TridentCore.Core.Engines.Deploying;
using TridentCore.Core.Engines.Deploying.Stages;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;
using TridentCore.Tests.Utilities;

namespace TridentCore.Tests;

[TestClass]
public sealed class NativeDeploymentTests
{
    [TestMethod]
    public async Task ExtractionReplacesDeclaredFilesWithoutClearingGameFiles()
    {
        using var sandbox = FixtureHelper.CreateSandbox();
        var archive = Path.Combine(sandbox.Root, "natives.jar");
        var timestamp = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using (var zip = new ZipArchive(File.Create(archive), ZipArchiveMode.Create))
        {
            WriteEntry("library.bin", "selected library");
            WriteEntry("META-INF/ignored.txt", "excluded");

            void WriteEntry(string name, string content)
            {
                var entry = zip.CreateEntry(name);
                entry.LastWriteTime = timestamp;
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        var context = CreateContext();
        var nativeDirectory = PathDef.Default.DirectoryOfNatives(context.Key);
        Directory.CreateDirectory(nativeDirectory);
        var library = Path.Combine(nativeDirectory, "library.bin");
        var gameFile = Path.Combine(nativeDirectory, "game-owned.bin");
        await File.WriteAllTextAsync(gameFile, "game data");
        var manifest = new EntityManifest();
        manifest.ExplosiveFiles.Add(new(archive, nativeDirectory, Excludes: ["META-INF/"]));
        context.Manifest = manifest;

        foreach (var previousTimestamp in new[] { timestamp.AddYears(10), timestamp })
        {
            await File.WriteAllTextAsync(library, "wrong library");
            File.SetLastWriteTimeUtc(library, previousTimestamp.UtcDateTime);
            using var stage = new SolidifyManifestStage(NullLogger<SolidifyManifestStage>.Instance, new OfflineFactory())
            {
                Context = context
            };
            await stage.ProcessAsync(CancellationToken.None);
            Assert.AreEqual("selected library", await File.ReadAllTextAsync(library));
            Assert.AreEqual("game data", await File.ReadAllTextAsync(gameFile));
            Assert.IsFalse(Directory.Exists(Path.Combine(nativeDirectory, "META-INF")));
        }
    }

    [TestMethod]
    public async Task ManifestExtractsOnlyExplicitNativesIntoEachInstancesBuildDirectory()
    {
        using var sandbox = FixtureHelper.CreateSandbox();
        var index = sandbox.Write("index.json", "{\"objects\":{}}"u8.ToArray());
        var component = FixtureHelper.Game() with
        {
            AssetIndex = new("test", new Uri(index)),
            Libraries = [new(FixtureHelper.Artifact("group:library:1:natives-osx-arm64")),
                new(FixtureHelper.Artifact("group:explicit:1:osx"), LaunchLibrary.Usage.Native)]
        };
        var compiled = new LaunchCompilerService().Compile(FixtureHelper.Resolution(component), FixtureHelper.Target).Result;
        var directories = new HashSet<string>();
        for (var i = 0; i < 2; i++)
        {
            var context = CreateContext();
            context.Lock = new LockData { Platform = new("1", null), Launch = compiled };
            using var stage = new GenerateManifestStage(new OfflineFactory()) { Context = context };
            await stage.ProcessAsync(CancellationToken.None);
            var extraction = context.Manifest!.ExplosiveFiles.Single();
            Assert.AreEqual(Path.Combine(PathDef.Default.DirectoryOfBuild(context.Key), "natives"), extraction.TargetDirectory);
            Assert.IsTrue(extraction.SourcePath.EndsWith("explicit-1-osx.jar", StringComparison.Ordinal));
            directories.Add(extraction.TargetDirectory);
        }
        Assert.AreEqual(2, directories.Count);
    }

    private static DeployContext CreateContext() => new(Guid.NewGuid().ToString("N"), new() { Version = "1" }, null!, new(),
        new(null, new Dictionary<string, LaunchDefinitionSnapshot.Entry>(), "empty"),
        (_, _) => throw new InvalidOperationException("This test does not resolve Java"));

    private sealed class OfflineFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("This test must not make HTTP requests");
    }
}
