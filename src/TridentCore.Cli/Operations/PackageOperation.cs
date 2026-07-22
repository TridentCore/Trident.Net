using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Abstractions.Utilities;
using TridentCore.Cli.Commands.Package;
using TridentCore.Cli.Services;
using TridentCore.Cli.Utilities;
using TridentCore.Core.Services;
using TridentCore.Pref;

namespace TridentCore.Cli.Operations;

internal static class PackageOperation
{
    public static async Task<PackageListResult> List(
        InstanceContextResolver resolver,
        RepositoryAgent repositories,
        string instance,
        string? profile,
        int index,
        int limit)
    {
        var ctx = resolver.Resolve(instance, profile);
        var entries = ctx.Profile.Setup.Packages;
        if (entries.Count == 0)
        {
            return new(ctx.Key, [], 0);
        }

        var resolved = await PackageDtos.ResolveEntriesAsync(entries, repositories, ctx).ConfigureAwait(false);
        var paged = resolved.Skip(index).Take(limit).ToArray();
        return new(ctx.Key, paged, resolved.Count);
    }

    public static async Task<PackageSearchLocalResult> SearchLocal(
        InstanceContextResolver resolver,
        RepositoryAgent repositories,
        string query,
        string? repository,
        ResourceKind? kind,
        string? instance,
        string? profile,
        int index,
        int limit)
    {
        var ctx = resolver.Resolve(instance!, profile);
        var entries = ctx.Profile.Setup.Packages;
        var resolved = entries.Count > 0
                           ? await PackageDtos.ResolveEntriesAsync(entries, repositories, ctx).ConfigureAwait(false)
                           : [];

        var matched = resolved
                     .Where(x => x.ProjectName?.Contains(query, StringComparison.OrdinalIgnoreCase) is true
                              || x.Author?.Contains(query, StringComparison.OrdinalIgnoreCase) is true
                              || x.Summary?.Contains(query, StringComparison.OrdinalIgnoreCase) is true
                              || x.Pref.Contains(query, StringComparison.OrdinalIgnoreCase))
                     .Where(x => kind is null || x.Kind == kind)
                     .Where(x => repository is null
                              || x.Pref.StartsWith($"{repository}:", StringComparison.OrdinalIgnoreCase))
                     .ToArray();

        var paged = matched.Skip(index).Take(limit).ToArray();
        return new(ctx.Key, paged, matched.Length);
    }

    public static async Task<PackageSearchRemoteResult> SearchRemote(
        RepositoryAgent repositories,
        string query,
        string repository,
        string? gameVersion,
        string? loader,
        ResourceKind? kind,
        int index,
        int limit)
    {
        var filter = PackageCliHelper.BuildFilter(gameVersion, loader, kind);
        var handle = await repositories.SearchAsync(repository, query, filter).ConfigureAwait(false);
        var items = new List<ExhibitDto>();

        await foreach (var item in PaginationHelper.FetchWindowAsync(handle, index, limit, CancellationToken.None))
        {
            items.Add(PackageDtos.FromExhibit(item));
        }

        return new(repository, (int)handle.TotalCount, items);
    }

    public static PackageAddResult Add(
        InstanceContextResolver resolver,
        ProfileManager profileManager,
        string pref,
        string instance,
        string? profile)
    {
        var ctx = resolver.Resolve(instance, profile);
        var guard = profileManager.GetMutable(ctx.Key);
        try
        {
            var parsed = PackageCliHelper.ParsePref(pref);
            var normalized = PackageHelper.ToPref(parsed.Repository, parsed.Namespace, parsed.Identity, parsed.Version);
            if (PackageCliHelper.ContainsProject(guard.Value, normalized))
            {
                return new(normalized, false, "already-installed", ctx.Key);
            }

            guard.Value.Setup.Packages.Add(new() { Enabled = true, Pref = normalized, Source = null });

            return new(normalized, true, null, ctx.Key);
        }
        finally
        {
            guard.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    public static async Task<PackageInspectResult> Inspect(
        InstanceContextResolver resolver,
        RepositoryAgent repositories,
        string pref,
        string? gameVersion,
        string? loader,
        string? kind,
        string? instance,
        string? profile)
    {
        var parsed = PackageCliHelper.ParsePref(pref);
        ResolvedInstanceContext? ctx = null;
        LocalPackageDto? local = null;
        if (resolver.TryResolve(instance, profile, out var resolved))
        {
            ctx = resolved;
            local = PackageDtos.FromEntry(PackageCliHelper.FindEntry(resolved.Profile, pref));
        }

        var filter = PackageCliHelper.BuildFilter(gameVersion, loader, PackageCliHelper.ParseKind(kind), ctx);
        var package = await repositories.ResolveAsync(parsed, filter).ConfigureAwait(false);
        var dto = PackageDtos.FromPackage(package);

        return new(ctx?.Key, local, dto);
    }

    public static PackageSetEnabledResult SetEnabled(
        InstanceContextResolver resolver,
        ProfileManager profileManager,
        string pref,
        string instance,
        bool enabled,
        string? profile)
    {
        var ctx = resolver.Resolve(instance, profile);
        var guard = profileManager.GetMutable(ctx.Key);
        try
        {
            var entry = PackageCliHelper.FindEntry(guard.Value, pref);
            entry.Enabled = enabled;
            return new(enabled ? "package.enable" : "package.disable", ctx.Key, entry.Pref, enabled);
        }
        finally
        {
            guard.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    public static async Task<PackageDependencyListResult> DependencyList(
        RepositoryAgent repositories,
        InstanceContextResolver resolver,
        string pref,
        string? gameVersion,
        string? loader,
        ResourceKind? kind,
        string? instance,
        string? profile)
    {
        var parsed = PackageCliHelper.ParsePref(pref);
        resolver.TryResolve(instance, profile, out var ctx);
        var filter = PackageCliHelper.BuildFilter(gameVersion, loader, kind, ctx);
        var package = await repositories.ResolveAsync(parsed, filter).ConfigureAwait(false);
        var dependencies = package.Dependencies.Select(PackageDtos.FromDependency).ToArray();
        return new(package.ToString(), dependencies);
    }

    public static async Task<PackageDependentListResult> DependentList(
        InstanceContextResolver resolver,
        RepositoryAgent repositories,
        string pref,
        string? gameVersion,
        string? loader,
        ResourceKind? kind,
        string instance,
        string? profile)
    {
        var ctx = resolver.Resolve(instance, profile);
        var target = PackageCliHelper.ParsePref(pref);
        var filter = PackageCliHelper.BuildFilter(gameVersion, loader, kind, ctx);

        var parsed = new List<(Profile.Rice.Entry Entry, PackageIdentifier Parsed)>();
        foreach (var entry in ctx.Profile.Setup.Packages.Where(x => x.Enabled))
        {
            if (PackageHelper.TryParse(entry.Pref, out var p))
            {
                parsed.Add((entry, p));
            }
        }

        if (parsed.Count == 0)
        {
            return new(ctx.Key, pref, [], []);
        }

        var identifiers = parsed.Select(x => x.Parsed).ToArray();

        var resolved = await repositories.ResolveBatchAsync(identifiers, filter).ConfigureAwait(false);

        var lookup = resolved.Successful.ToDictionary(x => (x.Key.Namespace, x.Key.Identity), x => x.Value);
        var dependents = new List<DependentDto>();
        var failed = new List<string>();

        foreach (var (entry, p) in parsed)
        {
            if (!lookup.TryGetValue((p.Namespace, p.Identity), out var package))
            {
                failed.Add(entry.Pref);
                continue;
            }

            if (package.Dependencies.Any(x => x.Label == target.Repository
                                           && x.Namespace == target.Namespace
                                           && x.ProjectId == target.Identity))
            {
                dependents.Add(new(entry.Pref, package.ProjectName, package.VersionName));
            }
        }

        return new(ctx.Key, pref, dependents, failed);
    }

    public static async Task<PackageVersionListResult> VersionList(
        RepositoryAgent repositories,
        string pref,
        string? gameVersion,
        string? loader,
        ResourceKind? kind,
        string sort,
        int index,
        int limit)
    {
        var parsed = PackageCliHelper.ParsePref(pref);
        var filter = PackageCliHelper.BuildFilter(gameVersion, loader, kind);
        var handle = await repositories.InspectAsync(parsed.ToProjectIdentifier(), filter).ConfigureAwait(false);

        var versions = new List<VersionDto>();
        await foreach (var version in PaginationHelper.FetchWindowAsync(handle, index, limit, CancellationToken.None))
        {
            versions.Add(PackageDtos.FromVersion(version));
        }

        versions = string.Equals(sort, "asc", StringComparison.OrdinalIgnoreCase)
                       ? [.. versions.OrderBy(x => x.PublishedAt)]
                       : [.. versions.OrderByDescending(x => x.PublishedAt)];

        return new(pref, (int)handle.TotalCount, versions);
    }

    public static PackageVersionSetResult VersionSet(
        InstanceContextResolver resolver,
        ProfileManager profileManager,
        string pref,
        string instance,
        string? profile)
    {
        var parsed = PackageCliHelper.ParsePref(pref);
        if (string.IsNullOrWhiteSpace(parsed.Version))
        {
            throw new CliException("A version pref with @version is required.", ExitCodes.USAGE);
        }

        var ctx = resolver.Resolve(instance, profile);
        var guard = profileManager.GetMutable(ctx.Key);
        try
        {
            var entry = PackageCliHelper.FindEntry(guard.Value, pref);
            var oldPref = entry.Pref;
            entry.Pref = PackageHelper.ToPref(parsed.Repository, parsed.Namespace, parsed.Identity, parsed.Version);
            return new(ctx.Key, oldPref, entry.Pref);
        }
        finally
        {
            guard.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}

internal sealed record PackageListResult(string Key, IReadOnlyList<ResolvedLocalPackageDto> Packages, int Total);

internal sealed record PackageSearchLocalResult(string Key, IReadOnlyList<ResolvedLocalPackageDto> Packages, int Total);

internal sealed record PackageAddResult(string Pref, bool Added, string? Reason, string Key);

internal sealed record PackageInspectResult(string? Key, LocalPackageDto? Local, PackageDto Package);

internal sealed record PackageSetEnabledResult(string Action, string Key, string Pref, bool Enabled);

internal sealed record PackageDependencyListResult(string Pref, IReadOnlyList<DependencyDto> Dependencies);

internal sealed record DependentDto(string Pref, string? ProjectName, string? VersionName);

internal sealed record PackageDependentListResult(
    string Key,
    string Target,
    IReadOnlyList<DependentDto> Dependents,
    IReadOnlyList<string> Failed);

internal sealed record PackageVersionListResult(string Pref, int Total, IReadOnlyList<VersionDto> Versions);

internal sealed record PackageSearchRemoteResult(string Repository, int Total, IReadOnlyList<ExhibitDto> Packages);

internal sealed record PackageVersionSetResult(string Key, string OldPref, string NewPref);
