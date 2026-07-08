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
    [McpServerTool(Name = "package_list"), Description("List packages installed in a Trident instance.")]
    public async Task<string> List(
        [Description("Instance key")] string instance,
        [Description("Number of results to skip")] int index = 0,
        [Description("Maximum number of results")] int limit = 20,
        [Description("Profile file path (optional)")] string? profile = null)
        => JsonSerializer.Serialize(await PackageOperation.List(resolver, repositories, instance, profile, index, limit), McpJson.Options);

    [McpServerTool(Name = "package_search"), Description("Search for packages in a remote repository or within an instance.")]
    public async Task<string> Search(
        [Description("Search query string")] string query,
        [Description("Repository label to search (required for remote search, optional for local)")] string? repository = null,
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

        if (repository is null)
        {
            return JsonSerializer.Serialize(
                new { error = "repository is required for remote search" }, McpJson.Options);
        }

        return JsonSerializer.Serialize(
            await PackageOperation.SearchRemote(repositories, query, repository, gameVersion, loader,
                TridentCore.Cli.Utilities.PackageCliHelper.ParseKind(kind), index, limit),
            McpJson.Options);
    }

    [McpServerTool(Name = "package_add"), Description("Add a package to a Trident instance by PREF.")]
    public string Add(
        [Description("Package PREF to add")] string pref,
        [Description("Instance key")] string instance,
        [Description("Profile file path (optional)")] string? profile = null)
        => JsonSerializer.Serialize(PackageOperation.Add(resolver, profileManager, pref, instance, profile), McpJson.Options);

    [McpServerTool(Name = "package_inspect"), Description("Inspect a package by PREF, optionally in the context of an instance.")]
    public async Task<string> Inspect(
        [Description("Package PREF")] string pref,
        [Description("Game version filter")] string? gameVersion = null,
        [Description("Loader identity filter")] string? loader = null,
        [Description("Package kind filter")] string? kind = null,
        [Description("Instance key (optional)")] string? instance = null,
        [Description("Profile file path (optional)")] string? profile = null)
        => JsonSerializer.Serialize(await PackageOperation.Inspect(resolver, repositories, pref, gameVersion, loader, kind, instance, profile), McpJson.Options);

    [McpServerTool(Name = "package_enable"), Description("Enable an installed package in an instance.")]
    public string Enable(
        [Description("Package PREF")] string pref,
        [Description("Instance key")] string instance,
        [Description("Profile file path (optional)")] string? profile = null)
        => JsonSerializer.Serialize(PackageOperation.SetEnabled(resolver, profileManager, pref, instance, true, profile), McpJson.Options);

    [McpServerTool(Name = "package_disable"), Description("Disable an installed package in an instance.")]
    public string Disable(
        [Description("Package PREF")] string pref,
        [Description("Instance key")] string instance,
        [Description("Profile file path (optional)")] string? profile = null)
        => JsonSerializer.Serialize(PackageOperation.SetEnabled(resolver, profileManager, pref, instance, false, profile), McpJson.Options);

    [McpServerTool(Name = "package_dependency_list"), Description("List dependencies of a package version.")]
    public async Task<string> DependencyList(
        [Description("Package PREF")] string pref,
        [Description("Game version filter (optional)")] string? gameVersion = null,
        [Description("Loader identity filter (optional)")] string? loader = null,
        [Description("Package kind filter (optional)")] string? kind = null,
        [Description("Instance key (optional)")] string? instance = null,
        [Description("Profile file path (optional)")] string? profile = null)
        => JsonSerializer.Serialize(await PackageOperation.DependencyList(repositories, resolver, pref, gameVersion, loader, TridentCore.Cli.Utilities.PackageCliHelper.ParseKind(kind), instance, profile), McpJson.Options);

    [McpServerTool(Name = "package_dependent_list"), Description("List packages in an instance that depend on a given package.")]
    public async Task<string> DependentList(
        [Description("Target package PREF")] string pref,
        [Description("Instance key")] string instance,
        [Description("Game version filter (optional)")] string? gameVersion = null,
        [Description("Loader identity filter (optional)")] string? loader = null,
        [Description("Package kind filter (optional)")] string? kind = null,
        [Description("Profile file path (optional)")] string? profile = null)
        => JsonSerializer.Serialize(await PackageOperation.DependentList(resolver, repositories, pref, gameVersion, loader, TridentCore.Cli.Utilities.PackageCliHelper.ParseKind(kind), instance, profile), McpJson.Options);

    [McpServerTool(Name = "package_version_list"), Description("List available versions of a package.")]
    public async Task<string> VersionList(
        [Description("Package PREF (project-level)")] string pref,
        [Description("Game version filter (optional)")] string? gameVersion = null,
        [Description("Loader identity filter (optional)")] string? loader = null,
        [Description("Package kind filter (optional)")] string? kind = null,
        [Description("Sort order: asc or desc")] string sort = "desc",
        [Description("Number of results to skip")] int index = 0,
        [Description("Maximum number of results")] int limit = 20)
        => JsonSerializer.Serialize(await PackageOperation.VersionList(repositories, pref, gameVersion, loader, TridentCore.Cli.Utilities.PackageCliHelper.ParseKind(kind), sort, index, limit), McpJson.Options);

    [McpServerTool(Name = "package_version_set"), Description("Set the version of an installed package.")]
    public string VersionSet(
        [Description("Package PREF with @version")] string pref,
        [Description("Instance key")] string instance,
        [Description("Profile file path (optional)")] string? profile = null)
        => JsonSerializer.Serialize(PackageOperation.VersionSet(resolver, profileManager, pref, instance, profile), McpJson.Options);
}
