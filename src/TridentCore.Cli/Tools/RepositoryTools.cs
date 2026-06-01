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
    [McpServerTool, Description("List all configured repositories.")]
    public string List()
        => JsonSerializer.Serialize(RepositoryOperation.List(userRepositories, combined), McpJson.Options);

    [McpServerTool, Description("Check repository status and capabilities.")]
    public async Task<string> Status(
        [Description("Repository label (optional)")] string? label = null)
        => JsonSerializer.Serialize(await RepositoryOperation.Status(repositories, label), McpJson.Options);
}
