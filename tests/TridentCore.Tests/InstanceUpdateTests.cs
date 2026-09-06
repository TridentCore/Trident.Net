using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Importers;
using TridentCore.Abstractions.Launching;
using TridentCore.Core.Facilities;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;
using TridentCore.Tests.Utilities;

namespace TridentCore.Tests;

[TestClass]
public sealed class InstanceUpdateTests
{
    [TestMethod]
    public async Task SuccessfulUpdatePublishesAllStateAndPreservesUserLayers()
    {
        using var fixture = new InstanceFixture();
        await using var stale = fixture.Profiles.GetMutable(fixture.Key);
        using var pack = CreatePack();
        await fixture.Service.ApplyAsync(fixture.Key, pack, Container(), CancellationToken.None);
        Assert.AreEqual("new attachment", fixture.Read("README.md"));
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Home, ".update")));
        Assert.AreEqual("new source", fixture.Read("import/config.txt"));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Home, "build/config.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Home, "data.lock.json")));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Home, "launch/import/old.txt")));
        Assert.AreEqual("user definition", fixture.Read("launch/user/keep.txt"));
        Assert.AreEqual("save", fixture.Read("persist/save.dat"));
        Assert.AreEqual("unmanaged save", fixture.Read("build/saves/world.dat"));
        Assert.AreEqual("native", fixture.Read("build/natives/game.bin"));
        Assert.AreEqual("prior recovery", fixture.Read(".import.old/keep.txt"));
        var profile = fixture.Profiles.GetImmutable(fixture.Key);
        Assert.AreEqual("Updated", profile.Name);
        Assert.AreEqual("pref://modrinth/pack@new", profile.Setup.Source);
        Assert.IsTrue(profile.Setup.Packages.Any(x => x.Pref == "pref://modrinth/manual@one"));
        var updatedPackage = profile.Setup.Packages.Single(x => x.Source == profile.Setup.Source);
        Assert.AreEqual("pref://modrinth/package@new", updatedPackage.Pref);
        Assert.IsFalse(updatedPackage.Enabled);
        Assert.AreSequenceEqual(new[] { "kept" }, updatedPackage.Tags);
        stale.Value.Name = "stale edit";
        await stale.DisposeAsync();
        Assert.IsTrue(fixture.Read("profile.json").Contains("Updated"));
    }

    [TestMethod]
    public async Task ProfilePromotionFailureRestoresFilesAndLeavesMemoryUnchanged()
    {
        using var fixture = new InstanceFixture();
        using var pack = CreatePack();
        var before = fixture.Snapshot();
        var original = fixture.Profiles.GetImmutable(fixture.Key);
        await Assert.ThrowsExactlyAsync<IOException>(() => fixture.Service.ApplyAsync(fixture.Key, pack, Container(),
            CancellationToken.None, new ProfileFailure()));
        Assert.AreSequenceEqual(before, fixture.Snapshot());
        Assert.AreSame(original, fixture.Profiles.GetImmutable(fixture.Key));
        Assert.AreEqual("Original", original.Name);
    }

    [TestMethod]
    public async Task FailedRollbackBlocksFurtherWritesAndKeepsOldSource()
    {
        using var fixture = new InstanceFixture();
        using var pack = CreatePack();
        await Assert.ThrowsExactlyAsync<FileTransaction.RecoveryException>(() => fixture.Service.ApplyAsync(
            fixture.Key, pack, Container(), CancellationToken.None, new ProfileFailure(failRestore: true)));
        Assert.AreEqual("old source", fixture.Read(".update/backup/import/config.txt"));
        Assert.AreEqual("player changes", fixture.Read("build/config.txt"));
        Assert.AreEqual("old attachment", fixture.Read("README.md"));
        Assert.ThrowsExactly<IOException>(() => fixture.Profiles.GetMutable(fixture.Key));
        var before = fixture.Snapshot();
        await Assert.ThrowsExactlyAsync<IOException>(() => fixture.Service.ApplyAsync(fixture.Key, pack, Container(), CancellationToken.None));
        Assert.AreSequenceEqual(before, fixture.Snapshot());
    }

    private static ImportedProfileContainer Container() => new(
        new Profile
        {
            Name = "Updated", Setup = new()
            {
                Version = "2", Source = "pref://modrinth/pack@new",
                Packages = [new() { Pref = "pref://modrinth/package@new", Enabled = true }]
            }
        }, [("config.txt", "config.txt")], [("README.md", "README.md")], null, [],
        [("import/definition.json", LaunchDefinitionHelper.Serialize(new LaunchDefinition
            { Components = [new() { Id = "game" }] })),
         ("import/components/game.json", LaunchDefinitionHelper.Serialize(FixtureHelper.Game()))],
        ReplacesLaunchImport: true);

    private static CompressedProfilePack CreatePack()
    {
        var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("config.txt").Open())) writer.Write("new source");
            using (var writer = new StreamWriter(zip.CreateEntry("README.md").Open())) writer.Write("new attachment");
        }
        memory.Position = 0;
        return new(memory);
    }

    private sealed class InstanceFixture : IDisposable
    {
        public ProfileManager Profiles { get; } = new(NullLogger<ProfileManager>.Instance);
        public string Key { get; }
        public string Home => PathDef.Default.DirectoryOfHome(Key);
        public InstanceModpackService Service { get; }

        public InstanceFixture()
        {
            using var key = Profiles.RequestKey(Guid.NewGuid().ToString("N"));
            Key = key.Key;
            Profiles.Add(key, new Profile
            {
                Name = "Original", Setup = new()
                {
                    Version = "1", Source = "pref://modrinth/pack@old", Packages =
                    [new() { Pref = "pref://modrinth/package@old", Enabled = false, Source = "pref://modrinth/pack@old", Tags = ["kept"] },
                     new() { Pref = "pref://modrinth/manual@one", Enabled = true }]
                }
            });
            Write("import/config.txt", "old source");
            Write("build/config.txt", "player changes");
            Write("build/saves/world.dat", "unmanaged save");
            Write("build/natives/game.bin", "native");
            Write("persist/save.dat", "save");
            Write("launch/import/old.txt", "old definition");
            Write("launch/user/keep.txt", "user definition");
            Write("README.md", "old attachment");
            Write("data.lock.json", "old lock");
            Write(".import.old/keep.txt", "prior recovery");
            Service = new(Profiles, new ImporterAgent([], NullLogger<ImporterAgent>.Instance),
                new FixtureHelper.OfflineHttpFactory(), NullLogger<InstanceModpackService>.Instance);
        }

        public string Read(string relative) => File.ReadAllText(Path.Combine(Home, relative));
        public void Write(string relative, string value)
        {
            var path = Path.Combine(Home, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, value);
        }
        public string[] Snapshot() => Directory.EnumerateFiles(Home, "*", SearchOption.AllDirectories)
            .Select(x => Path.GetRelativePath(Home, x) + ":" + Convert.ToHexString(File.ReadAllBytes(x)))
            .Order(StringComparer.Ordinal).ToArray();
        public void Dispose()
        {
            Profiles.Dispose();
            Directory.Delete(Home, true);
        }
    }

    private sealed class ProfileFailure(bool failRestore = false) : FileTransaction.FileOperations
    {
        public override void Move(string source, string target, FileTransaction.EntryKind kind)
        {
            if (source.EndsWith(Path.Combine("staged", "profile.json"), StringComparison.Ordinal)
             || (failRestore && source.EndsWith(Path.Combine("backup", "import"), StringComparison.Ordinal)))
                throw new IOException("Injected commit or rollback failure");
            base.Move(source, target, kind);
        }
    }
}
