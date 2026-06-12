using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;

namespace TridentCore.Cli.Tools;

[McpServerToolType]
public class AccountTools(AccountStore accounts)
{
    [McpServerTool(Name = "account_list"), Description("List all registered accounts.")]
    public string List()
        => JsonSerializer.Serialize(AccountOperation.List(accounts), McpJson.Options);

    [McpServerTool(Name = "account_add_offline"), Description("Add an offline account.")]
    public string AddOffline(
        [Description("Username for the offline account")] string username,
        [Description("UUID for the offline account (optional)")] string? uuid = null)
        => JsonSerializer.Serialize(AccountOperation.AddOffline(accounts, username, uuid), McpJson.Options);

    [McpServerTool(Name = "account_remove"), Description("Remove a registered account by UUID.")]
    public string Remove(
        [Description("Account UUID to remove")] string uuid)
        => JsonSerializer.Serialize(AccountOperation.Remove(accounts, uuid), McpJson.Options);
}
