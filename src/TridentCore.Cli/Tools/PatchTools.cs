using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;

namespace TridentCore.Cli.Tools;

[McpServerToolType]
public class PatchTools(InstanceContextResolver resolver)
{
    [McpServerTool(Name = "patch_list")]
    [Description("List the ordered import and users patch entries of an instance without changing its profile.")]
    public async Task<string> List([Description("Instance key")] string instance) =>
        JsonSerializer.Serialize(await PatchOperation.ExecuteAsync(resolver.Resolve(instance, null).Key, "list"), McpJson.Options);

    [McpServerTool(Name = "patch_set_enabled")]
    [Description("Enable or disable an indexed native patch while retaining its order.")]
    public async Task<string> SetEnabled([Description("Instance key")] string instance,
        [Description("Patch path relative to its layer")] string path,
        [Description("Whether to enable the patch")] bool enabled,
        [Description("import or users")] string layer = "users") =>
        JsonSerializer.Serialize(await PatchOperation.ExecuteAsync(resolver.Resolve(instance, null).Key,
            enabled ? "enable" : "disable", layer, path), McpJson.Options);

    [McpServerTool(Name = "patch_move")]
    [Description("Move a native patch to a zero-based position within its existing layer.")]
    public async Task<string> Move([Description("Instance key")] string instance,
        [Description("Patch path relative to its layer")] string path,
        [Description("Zero-based destination position")] int position,
        [Description("import or users")] string layer = "users") =>
        JsonSerializer.Serialize(await PatchOperation.ExecuteAsync(resolver.Resolve(instance, null).Key,
            "move", layer, path, position: position), McpJson.Options);
}
