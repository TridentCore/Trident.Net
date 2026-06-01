using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Tools;

[McpServerToolType]
public class RepositoryTools(UserRepositoryStore userRepositories, CliRepositoryProviderAccessor combined, RepositoryAgent repositories)
{
    [McpServerTool(Name = "trident_repository_list"), Description("List all configured repositories.")]
    public string List()
        => JsonSerializer.Serialize(RepositoryOperation.List(userRepositories, combined), McpJson.Options);

    [McpServerTool(Name = "trident_repository_status"), Description("Check repository status and capabilities.")]
    public async Task<string> Status(
        [Description("Repository label (optional)")] string? label = null)
        => JsonSerializer.Serialize(await RepositoryOperation.Status(repositories, label), McpJson.Options);

    [McpServerTool(Name = "trident_repository_add"), Description("Add or replace a user-configured repository.")]
    public string Add(
        [Description("Repository label")] string label,
        [Description("Repository endpoint URI")] string endpoint,
        [Description("Driver name (optional, defaults to label)")] string? driver = null,
        [Description("API key (optional)")] string? apiKey = null,
        [Description("User agent (optional)")] string? userAgent = null)
        => JsonSerializer.Serialize(RepositoryOperation.Add(userRepositories, label, driver, endpoint, apiKey, userAgent), McpJson.Options);

    [McpServerTool(Name = "trident_repository_remove"), Description("Remove a user-configured repository.")]
    public string Remove(
        [Description("Repository label")] string label)
        => JsonSerializer.Serialize(RepositoryOperation.Remove(userRepositories, label), McpJson.Options);
}
