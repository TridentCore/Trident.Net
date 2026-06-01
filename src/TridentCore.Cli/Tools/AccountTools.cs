using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;

namespace TridentCore.Cli.Tools;

[McpServerToolType]
public class AccountTools(AccountStore accounts)
{
    [McpServerTool, Description("List all registered accounts.")]
    public string List()
        => JsonSerializer.Serialize(AccountOperation.List(accounts), McpJson.Options);
}
