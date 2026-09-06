using System.Text.Json;
using TridentCore.Abstractions;
using TridentCore.Abstractions.Launching;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Models.PrismLauncherApi;
using TridentCore.Core.Utilities;

namespace TridentCore.Tests.Utilities;

internal static class FixtureHelper
{
    public static readonly LaunchTarget Target = new("osx", "arm64", "15.0", 17);

    public static LaunchComponent Game(string id = "game") => new()
    {
        Id = id, Version = "1", MainClass = "game.Main", JavaMajors = [17u, 21u],
        AssetIndex = new("test", new("https://example.invalid/index.json")),
        GameArguments = new() { Replace = [new("--username"), new("${auth_player_name}")] }
    };

    public static LaunchArtifact Artifact(string coordinate) =>
        new(ArtifactHelper.Parse(coordinate), new Uri("https://example.invalid/" + Uri.EscapeDataString(coordinate) + ".jar"));

    public static LaunchResolution Resolution(params LaunchComponent[] components) =>
        new([.. components.Select(x => new ResolvedLaunchComponent(x, "fixture:" + x.Id))], [17u, 21u],
            HashHelper.ComputeObjectHash(components));

    public static Component Metadata(string name) => JsonSerializer.Deserialize<Component>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)), JsonSerializerOptions.Web)!;

    public static Sandbox CreateSandbox() => new();

    public sealed class Sandbox : IDisposable
    {
        public string Root { get; } = Path.Combine(AppContext.BaseDirectory, "scratch", Guid.NewGuid().ToString("N"));
        public Sandbox() => Directory.CreateDirectory(Root);
        public string Write(string relative, byte[] content)
        {
            var path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
            return path;
        }
        public string WriteJson<T>(string relative, T value) => Write(relative, LaunchDefinitionHelper.Serialize(value));
        public void Dispose() => Directory.Delete(Root, true);
    }

    public sealed class OfflineHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Unexpected HTTP request");
    }

    public sealed class Provider(params LaunchComponent[] components) : ILaunchComponentProvider
    {
        public List<string> Requests { get; } = [];
        public Task<LaunchComponent> ResolveAsync(string id, string version, string minecraftVersion, CancellationToken token)
        {
            Requests.Add(id);
            return Task.FromResult(components.Single(x => x.Id == id && x.Version == version));
        }
    }
}
