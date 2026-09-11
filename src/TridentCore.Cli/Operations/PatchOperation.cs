using TridentCore.Abstractions.FileModels;
using TridentCore.Cli.Services;
using TridentCore.Core.Utilities;

namespace TridentCore.Cli.Operations;

public static class PatchOperation
{
    public static async Task<PatchIndex> ExecuteAsync(string instance, string action,
        string layer = "users", string? path = null, string? name = null, int? position = null, CancellationToken token = default)
    {
        string RequiredPath() => path ?? throw new CliException("This action requires --path.", ExitCodes.USAGE);
        switch (action)
        {
            case "list":
                break;
            case "add":
                if (layer != "users")
                {
                    throw new CliException("New custom patches belong to the users layer.", ExitCodes.USAGE);
                }
                await PatchStorageHelper.AddUserAsync(instance, RequiredPath(), name ?? throw new CliException("Add requires --name.", ExitCodes.USAGE), token).ConfigureAwait(false);
                break;
            case "enable":
            case "disable":
                await PatchStorageHelper.UpdateEntryAsync(instance, layer, RequiredPath(), action == "enable", token: token).ConfigureAwait(false);
                break;
            case "move":
                await PatchStorageHelper.UpdateEntryAsync(instance, layer, RequiredPath(), position: position ?? throw new CliException("Move requires --position.", ExitCodes.USAGE), token: token).ConfigureAwait(false);
                break;
            case "remove":
                await PatchStorageHelper.RemoveAsync(instance, layer, RequiredPath(), token).ConfigureAwait(false);
                break;
            default:
                throw new CliException("Patch action must be list, add, enable, disable, move or remove.", ExitCodes.USAGE);
        }
        return await PatchStorageHelper.ReadIndexAsync(instance, token).ConfigureAwait(false);
    }
}
