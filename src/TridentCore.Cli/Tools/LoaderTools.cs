using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Tools;

[McpServerToolType]
public class LoaderTools(PrismLauncherService prismLauncher, InstanceContextResolver resolver, ProfileManager profileManager)
{
    [McpServerTool(Name = "trident_loader_list"), Description("List all supported loaders.")]
    public string List()
        => JsonSerializer.Serialize(LoaderOperation.List(), McpJson.Options);

    [McpServerTool(Name = "trident_loader_version_list"), Description("List available versions of a loader for a Minecraft version.")]
    public async Task<string> VersionList(
        [Description("Loader identity (e.g. fabric, forge, quilt)")] string loaderId,
        [Description("Minecraft version")] string version,
        [Description("Sort order: asc or desc")] string sort = "desc",
        [Description("Number of results to skip")] int index = 0,
        [Description("Maximum number of results")] int limit = 20)
        => JsonSerializer.Serialize(await LoaderOperation.VersionList(prismLauncher, loaderId, version, sort, index, limit), McpJson.Options);

    [McpServerTool(Name = "trident_loader_get"), Description("Get the loader configuration for an instance.")]
    public string Get(
        [Description("Instance key")] string instance,
        [Description("Profile file path (optional)")] string? profile = null)
        => JsonSerializer.Serialize(LoaderOperation.Get(resolver, instance, profile), McpJson.Options);

    [McpServerTool(Name = "trident_loader_set"), Description("Set the loader for an instance.")]
    public string Set(
        [Description("Loader LURL, e.g. fabric:0.16.0")] string loader,
        [Description("Instance key")] string instance,
        [Description("Profile file path (optional)")] string? profile = null)
        => JsonSerializer.Serialize(LoaderOperation.Set(resolver, profileManager, loader, instance, profile), McpJson.Options);
}
