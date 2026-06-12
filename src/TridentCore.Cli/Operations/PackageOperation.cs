using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Abstractions.Utilities;
using TridentCore.Cli.Commands.Package;
using TridentCore.Cli.Services;
using TridentCore.Cli.Utilities;
using TridentCore.Core.Services;
using TridentCore.Purl;

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
            .Where(x =>
                (x.ProjectName?.Contains(query, StringComparison.OrdinalIgnoreCase) is true)
                || (x.Author?.Contains(query, StringComparison.OrdinalIgnoreCase) is true)
                || (x.Summary?.Contains(query, StringComparison.OrdinalIgnoreCase) is true)
                || x.Purl.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Where(x => kind is null || x.Kind == kind)
            .Where(x => repository is null || x.Purl.StartsWith($"{repository}:", StringComparison.OrdinalIgnoreCase))
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
        string purl,
        string instance,
        string? profile)
    {
        var ctx = resolver.Resolve(instance, profile);
        var guard = profileManager.GetMutable(ctx.Key);
        try
        {
            var parsed = PackageCliHelper.ParsePurl(purl);
            var normalized = PackageHelper.ToPurl(parsed.Label, parsed.Namespace, parsed.Pid, parsed.Vid);
            if (PackageCliHelper.ContainsProject(guard.Value, normalized))
            {
                return new(normalized, false, "already-installed", ctx.Key);
            }

            guard.Value.Setup.Packages.Add(new()
            {
                Enabled = true,
                Purl = normalized,
                Source = null,
            });

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
        string purl,
        string? gameVersion,
        string? loader,
        string? kind,
        string? instance,
        string? profile)
    {
        var parsed = PackageCliHelper.ParsePurl(purl);
        ResolvedInstanceContext? ctx = null;
        LocalPackageDto? local = null;
        if (resolver.TryResolve(instance, profile, out var resolved))
        {
            ctx = resolved;
            local = PackageDtos.FromEntry(PackageCliHelper.FindEntry(resolved.Profile, purl));
        }

        var filter = PackageCliHelper.BuildFilter(gameVersion, loader, PackageCliHelper.ParseKind(kind), ctx);
        var package = await repositories.ResolveAsync(parsed.Label, parsed.Namespace, parsed.Pid, parsed.Vid, filter).ConfigureAwait(false);
        var dto = PackageDtos.FromPackage(package);

        return new(ctx?.Key, local, dto);
    }

    public static PackageSetEnabledResult SetEnabled(
        InstanceContextResolver resolver,
        ProfileManager profileManager,
        string purl,
        string instance,
        bool enabled,
        string? profile)
    {
        var ctx = resolver.Resolve(instance, profile);
        var guard = profileManager.GetMutable(ctx.Key);
        try
        {
            var entry = PackageCliHelper.FindEntry(guard.Value, purl);
            entry.Enabled = enabled;
            return new(enabled ? "package.enable" : "package.disable", ctx.Key, entry.Purl, enabled);
        }
        finally
        {
            guard.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    public static async Task<PackageDependencyListResult> DependencyList(
        RepositoryAgent repositories,
        InstanceContextResolver resolver,
        string purl,
        string? gameVersion,
        string? loader,
        ResourceKind? kind,
        string? instance,
        string? profile)
    {
        var parsed = PackageCliHelper.ParsePurl(purl);
        resolver.TryResolve(instance, profile, out var ctx);
        var filter = PackageCliHelper.BuildFilter(gameVersion, loader, kind, ctx);
        var package = await repositories.ResolveAsync(parsed.Label, parsed.Namespace, parsed.Pid, parsed.Vid, filter).ConfigureAwait(false);
        var dependencies = package.Dependencies.Select(PackageDtos.FromDependency).ToArray();
        return new(package.ToString(), dependencies);
    }

    public static async Task<PackageDependentListResult> DependentList(
        InstanceContextResolver resolver,
        RepositoryAgent repositories,
        string purl,
        string? gameVersion,
        string? loader,
        ResourceKind? kind,
        string instance,
        string? profile)
    {
        var ctx = resolver.Resolve(instance, profile);
        var target = PackageCliHelper.ParsePurl(purl);
        var filter = PackageCliHelper.BuildFilter(gameVersion, loader, kind, ctx);

        var parsed = new List<(Profile.Rice.Entry Entry, (string Label, string? Namespace, string Pid, string? Vid) Parsed)>();
        foreach (var entry in ctx.Profile.Setup.Packages.Where(x => x.Enabled))
        {
            if (PackageHelper.TryParse(entry.Purl, out var p))
            {
                parsed.Add((entry, p));
            }
        }

        if (parsed.Count == 0)
        {
            return new(ctx.Key, purl, [], []);
        }

        var identifiers = parsed
            .Select(x => new PackageIdentifier(x.Parsed.Label, x.Parsed.Namespace, x.Parsed.Pid, x.Parsed.Vid))
            .ToArray();

        IReadOnlyList<(PackageIdentifier, Package)> resolved;
        try
        {
            resolved = await repositories
                .ResolveBatchAsync(identifiers, filter)
                .ConfigureAwait(false);
        }
        catch
        {
            return new(ctx.Key, purl, [], parsed.Select(x => x.Entry.Purl).ToArray());
        }

        var lookup = resolved.ToDictionary(
            x => (x.Item1.Namespace, x.Item1.Identity),
            x => x.Item2
        );
        var dependents = new List<DependentDto>();
        var failed = new List<string>();

        foreach (var (entry, p) in parsed)
        {
            if (!lookup.TryGetValue((p.Namespace, p.Pid), out var package))
            {
                failed.Add(entry.Purl);
                continue;
            }

            if (package.Dependencies.Any(x =>
                    x.Label == target.Label
                    && x.Namespace == target.Namespace
                    && x.ProjectId == target.Pid))
            {
                dependents.Add(new(entry.Purl, package.ProjectName, package.VersionName));
            }
        }

        return new(ctx.Key, purl, dependents, failed);
    }

    public static async Task<PackageVersionListResult> VersionList(
        RepositoryAgent repositories,
        string purl,
        string? gameVersion,
        string? loader,
        ResourceKind? kind,
        string sort,
        int index,
        int limit)
    {
        var parsed = PackageCliHelper.ParsePurl(purl);
        var filter = PackageCliHelper.BuildFilter(gameVersion, loader, kind);
        var handle = await repositories
            .InspectAsync(parsed.Label, parsed.Namespace, parsed.Pid, filter)
            .ConfigureAwait(false);

        var versions = new List<VersionDto>();
        await foreach (var version in PaginationHelper.FetchWindowAsync(handle, index, limit, CancellationToken.None))
        {
            versions.Add(PackageDtos.FromVersion(version));
        }

        versions = string.Equals(sort, "asc", StringComparison.OrdinalIgnoreCase)
            ? [.. versions.OrderBy(x => x.PublishedAt)]
            : [.. versions.OrderByDescending(x => x.PublishedAt)];

        return new(purl, (int)handle.TotalCount, versions);
    }

    public static PackageVersionSetResult VersionSet(
        InstanceContextResolver resolver,
        ProfileManager profileManager,
        string purl,
        string instance,
        string? profile)
    {
        var parsed = PackageCliHelper.ParsePurl(purl);
        if (string.IsNullOrWhiteSpace(parsed.Vid))
        {
            throw new CliException("A version purl with @version is required.", ExitCodes.USAGE);
        }

        var ctx = resolver.Resolve(instance, profile);
        var guard = profileManager.GetMutable(ctx.Key);
        try
        {
            var entry = PackageCliHelper.FindEntry(guard.Value, purl);
            var oldPurl = entry.Purl;
            entry.Purl = PackageHelper.ToPurl(parsed.Label, parsed.Namespace, parsed.Pid, parsed.Vid);
            return new(ctx.Key, oldPurl, entry.Purl);
        }
        finally
        {
            guard.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}

internal sealed record PackageListResult(string Key, IReadOnlyList<ResolvedLocalPackageDto> Packages, int Total);
internal sealed record PackageSearchLocalResult(string Key, IReadOnlyList<ResolvedLocalPackageDto> Packages, int Total);
internal sealed record PackageAddResult(string Purl, bool Added, string? Reason, string Key);
internal sealed record PackageInspectResult(string? Key, LocalPackageDto? Local, PackageDto Package);
internal sealed record PackageSetEnabledResult(string Action, string Key, string Purl, bool Enabled);
internal sealed record PackageDependencyListResult(string Purl, IReadOnlyList<DependencyDto> Dependencies);
internal sealed record DependentDto(string Purl, string? ProjectName, string? VersionName);
internal sealed record PackageDependentListResult(string Key, string Target, IReadOnlyList<DependentDto> Dependents, IReadOnlyList<string> Failed);
internal sealed record PackageVersionListResult(string Purl, int Total, IReadOnlyList<VersionDto> Versions);
internal sealed record PackageSearchRemoteResult(string Repository, int Total, IReadOnlyList<ExhibitDto> Packages);
internal sealed record PackageVersionSetResult(string Key, string OldPurl, string NewPurl);
