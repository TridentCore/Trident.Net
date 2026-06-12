using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Tools;

[McpServerToolType]
public class ConfigTools(InstanceContextResolver resolver, CliConfigurationStore configuration, ProfileManager profileManager)
{
    [McpServerTool(Name = "config_get"), Description("Get a configuration value by name.")]
    public string Get(
        [Description("Configuration key name")] string name,
        [Description("Instance key (optional)")] string? instance = null,
        [Description("Profile file path (optional)")] string? profile = null)
        => JsonSerializer.Serialize(ConfigOperation.Get(resolver, configuration, name, instance, profile), McpJson.Options);

    [McpServerTool(Name = "config_set"), Description("Set a configuration value.")]
    public string Set(
        [Description("Configuration key name")] string name,
        [Description("Configuration value")] string value,
        [Description("Value type: auto, string, bool, integer, number")] string? type = null,
        [Description("Instance key (optional)")] string? instance = null,
        [Description("Profile file path (optional)")] string? profile = null)
        => JsonSerializer.Serialize(ConfigOperation.Set(resolver, configuration, profileManager, name, value, type, instance, profile), McpJson.Options);

    [McpServerTool(Name = "config_unset"), Description("Remove a configuration value.")]
    public string Unset(
        [Description("Configuration key name")] string name,
        [Description("Instance key (optional)")] string? instance = null,
        [Description("Profile file path (optional)")] string? profile = null)
    {
        ConfigOperation.Unset(resolver, configuration, profileManager, name, instance, profile);
        return JsonSerializer.Serialize(new { action = "config.unset", name }, McpJson.Options);
    }

    [McpServerTool(Name = "config_list"), Description("List all configuration values.")]
    public string List(
        [Description("Instance key (optional)")] string? instance = null,
        [Description("Profile file path (optional)")] string? profile = null)
        => JsonSerializer.Serialize(ConfigOperation.List(resolver, configuration, instance, profile), McpJson.Options);
}
