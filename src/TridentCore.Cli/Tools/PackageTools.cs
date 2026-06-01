using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Tools;

[McpServerToolType]
public class PackageTools(InstanceContextResolver resolver, RepositoryAgent repositories, ProfileManager profileManager)
{
    [McpServerTool, Description("List packages installed in a Trident instance.")]
    public async Task<string> List(
        [Description("Instance key")] string instance,
        [Description("Number of results to skip")] int index = 0,
        [Description("Maximum number of results")] int limit = 20,
        [Description("Profile file path (optional)")] string? profile = null)
        => JsonSerializer.Serialize(await PackageOperation.List(resolver, repositories, instance, profile, index, limit), McpJson.Options);

    [McpServerTool, Description("Search for packages in remote repositories or within an instance.")]
    public async Task<string> Search(
        [Description("Search query string")] string query,
        [Description("Repository label to search (optional)")] string? repository = null,
        [Description("Game version filter")] string? gameVersion = null,
        [Description("Loader identity filter")] string? loader = null,
        [Description("Package kind filter")] string? kind = null,
        [Description("Number of results to skip")] int index = 0,
        [Description("Maximum number of results")] int limit = 20,
        [Description("Instance key for local search (optional)")] string? instance = null,
        [Description("Profile file path (optional)")] string? profile = null)
    {
        if (instance is not null && resolver.TryResolve(instance, profile, out _))
        {
            return JsonSerializer.Serialize(
                await PackageOperation.SearchLocal(resolver, repositories, query, repository,
                    TridentCore.Cli.Utilities.PackageCliHelper.ParseKind(kind), instance, profile, index, limit),
                McpJson.Options);
        }

        return JsonSerializer.Serialize(
            await PackageOperation.SearchRemote(repositories, query, repository, gameVersion, loader,
                TridentCore.Cli.Utilities.PackageCliHelper.ParseKind(kind), index, limit),
            McpJson.Options);
    }

    [McpServerTool, Description("Add a package to a Trident instance by PURL.")]
    public string Add(
        [Description("Package PURL to add")] string purl,
        [Description("Instance key")] string instance,
        [Description("Profile file path (optional)")] string? profile = null)
        => JsonSerializer.Serialize(PackageOperation.Add(resolver, profileManager, purl, instance, profile), McpJson.Options);

    [McpServerTool, Description("Inspect a package by PURL, optionally in the context of an instance.")]
    public async Task<string> Inspect(
        [Description("Package PURL")] string purl,
        [Description("Game version filter")] string? gameVersion = null,
        [Description("Loader identity filter")] string? loader = null,
        [Description("Package kind filter")] string? kind = null,
        [Description("Instance key (optional)")] string? instance = null,
        [Description("Profile file path (optional)")] string? profile = null)
        => JsonSerializer.Serialize(await PackageOperation.Inspect(resolver, repositories, purl, gameVersion, loader, kind, instance, profile), McpJson.Options);

    [McpServerTool, Description("Enable or disable an installed package in an instance.")]
    public string SetEnabled(
        [Description("Package PURL")] string purl,
        [Description("Instance key")] string instance,
        [Description("Enable or disable")] bool enabled,
        [Description("Profile file path (optional)")] string? profile = null)
        => JsonSerializer.Serialize(PackageOperation.SetEnabled(resolver, profileManager, purl, instance, enabled, profile), McpJson.Options);
}
