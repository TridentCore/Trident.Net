using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Tools;

[McpServerToolType]
public class InstanceTools(ProfileManager profileManager, InstanceContextResolver resolver, RepositoryAgent repositories)
{
    [McpServerTool, Description("List all Trident instances.")]
    public string List()
        => JsonSerializer.Serialize(InstanceOperation.List(profileManager), McpJson.Options);

    [McpServerTool, Description("Inspect a Trident instance with package preview.")]
    public async Task<string> Inspect(
        [Description("Instance key")] string instance,
        [Description("Profile file path (optional)")] string? profile = null)
        => JsonSerializer.Serialize(await InstanceOperation.Inspect(resolver, repositories, instance, profile), McpJson.Options);
}
