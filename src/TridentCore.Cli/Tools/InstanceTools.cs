using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Tools;

[McpServerToolType]
public class InstanceTools(ProfileManager profileManager, InstanceContextResolver resolver, RepositoryAgent repositories, InstanceManager instanceManager, ImporterAgent importerAgent, ExporterAgent exporterAgent)
{
    [McpServerTool(Name = "instance_list"), Description("List all Trident instances.")]
    public string List()
        => JsonSerializer.Serialize(InstanceOperation.List(profileManager), McpJson.Options);

    [McpServerTool(Name = "instance_inspect"), Description("Inspect a Trident instance with package preview.")]
    public async Task<string> Inspect(
        [Description("Instance key")] string instance,
        [Description("Profile file path (optional)")] string? profile = null)
        => JsonSerializer.Serialize(await InstanceOperation.Inspect(resolver, repositories, instance, profile), McpJson.Options);

    [McpServerTool(Name = "instance_create"), Description("Create a new Trident instance.")]
    public string Create(
        [Description("Instance display name")] string name,
        [Description("Minecraft game version")] string version,
        [Description("Loader LURL, e.g. fabric:0.16.0 (optional)")] string? loader = null,
        [Description("Instance identity key (optional, defaults to name)")] string? identity = null)
        => JsonSerializer.Serialize(InstanceOperation.Create(profileManager, name, version, loader, identity), McpJson.Options);

    [McpServerTool(Name = "instance_unlock"), Description("Unlock an instance from its import source.")]
    public string Unlock(
        [Description("Instance key")] string instance,
        [Description("Profile file path (optional)")] string? profile = null)
        => JsonSerializer.Serialize(InstanceOperation.Unlock(resolver, profileManager, instance, profile), McpJson.Options);

    [McpServerTool(Name = "instance_delete"), Description("Delete a Trident instance.")]
    public string Delete(
        [Description("Instance key")] string instance,
        [Description("Profile file path (optional)")] string? profile = null)
        => JsonSerializer.Serialize(InstanceOperation.Delete(resolver, profileManager, instance, profile), McpJson.Options);

    [McpServerTool(Name = "instance_reset"), Description("Reset build artifacts for a Trident instance.")]
    public string Reset(
        [Description("Instance key")] string instance,
        [Description("Profile file path (optional)")] string? profile = null)
        => JsonSerializer.Serialize(InstanceOperation.Reset(resolver, instanceManager, instance, profile), McpJson.Options);

    [McpServerTool(Name = "instance_export"), Description("Export a Trident instance to a pack file.")]
    public async Task<string> Export(
        [Description("Instance key")] string instance,
        [Description("Output file path")] string output,
        [Description("Author name")] string author,
        [Description("Export format (trident, modrinth, curseforge, multimc)")] string format = "trident",
        [Description("Export type (online or offline)")] string type = "online",
        [Description("Pack name (optional)")] string? name = null,
        [Description("Pack version")] string version = "1.0.0",
        [Description("Exclude tags")] bool noTags = false,
        [Description("Profile file path (optional)")] string? profile = null)
        => JsonSerializer.Serialize(await InstanceOperation.ExportAsync(resolver, exporterAgent, instance, profile, format, type, name, author, version, output, noTags), McpJson.Options);

    [McpServerTool(Name = "instance_import"), Description("Import a pack file as a new Trident instance.")]
    public async Task<string> Import(
        [Description("Path to pack file")] string path,
        [Description("Instance name (optional)")] string? name = null,
        [Description("Instance identity key (optional)")] string? identity = null)
        => JsonSerializer.Serialize(await InstanceOperation.ImportAsync(profileManager, importerAgent, path, name, identity), McpJson.Options);

    [McpServerTool(Name = "instance_install"), Description("Install a modpack from a repository as a new Trident instance.")]
    public async Task<string> Install(
        [Description("Modpack PREF")] string pref,
        [Description("Instance identity key (optional)")] string? identity = null)
    {
        var tracker = await InstanceOperation
            .StartInstallAsync(instanceManager, repositories, pref, identity)
            .ConfigureAwait(false);
        await TrackerAwaiter.AwaitCompletionAsync(tracker, CancellationToken.None)
            .ConfigureAwait(false);
        TrackerAwaiter.ThrowIfFaulted(tracker, "Install failed.");
        return JsonSerializer.Serialize(new { key = tracker.Key, source = tracker.Reference }, McpJson.Options);
    }
}
