using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using TridentCore.Cli.Operations;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Tools;

[McpServerToolType]
public class LoaderTools(PrismLauncherService prismLauncher)
{
    [McpServerTool, Description("List all supported loaders.")]
    public string List()
        => JsonSerializer.Serialize(LoaderOperation.List(), McpJson.Options);

    [McpServerTool, Description("List available versions of a loader for a Minecraft version.")]
    public async Task<string> VersionList(
        [Description("Loader identity (e.g. fabric, forge, quilt)")] string loaderId,
        [Description("Minecraft version")] string version,
        [Description("Sort order: asc or desc")] string sort = "desc",
        [Description("Number of results to skip")] int index = 0,
        [Description("Maximum number of results")] int limit = 20)
        => JsonSerializer.Serialize(await LoaderOperation.VersionList(prismLauncher, loaderId, version, sort, index, limit), McpJson.Options);
}
