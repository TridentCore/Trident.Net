using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Pref;
using Version = TridentCore.Abstractions.Repositories.Resources.Version;

namespace TridentCore.Abstractions.Repositories;

public interface IRepository
{
    Task<RepositoryStatus> CheckStatusAsync();
    Task<IPaginationHandle<Exhibit>> SearchAsync(string query, Filter filter);
    Task<Package> IdentifyAsync(ReadOnlyMemory<byte> content);
    Task<Project> QueryAsync(string? ns, string pid);
    Task<BatchResolveResult<(string?, string pid), Project>> QueryBatchAsync(
        IEnumerable<(string?, string pid)> batch
    );
    Task<Package> ResolveAsync(string? ns, string pid, string? vid, Filter filter);

    Task<BatchResolveResult<ScopedPackageIdentifier, Package>> ResolveBatchAsync(
        IEnumerable<ScopedPackageIdentifier> batch,
        Filter filter
    );

    Task<string> ReadDescriptionAsync(string? ns, string pid);
    Task<string> ReadChangelogAsync(string? ns, string pid, string vid);
    Task<IPaginationHandle<Version>> InspectAsync(string? ns, string pid, Filter filter);

    // Hidden repositories stay registered and resolvable by label, but are excluded from
    // RepositoryAgent.Labels so they never appear in browse/search/marketplace lists.
    bool IsHidden => false;
}
