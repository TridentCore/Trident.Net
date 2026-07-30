using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Pref;
using Version = TridentCore.Abstractions.Repositories.Resources.Version;

namespace TridentCore.Abstractions.Repositories;

public interface IRepository
{
    // Hidden repositories stay registered and resolvable by label, but are excluded from
    // RepositoryAgent.Labels so they never appear in browse/search/marketplace lists.
    bool IsHidden => false;
    Task<RepositoryStatus> CheckStatusAsync();
    Task<IPaginationHandle<Exhibit>> SearchAsync(string query, Filter filter);
    Task<Package> IdentifyAsync(ReadOnlyMemory<byte> content);

    // NOTE: batch counterpart of IdentifyAsync. The repository queries its native batch endpoint with
    //  exactly what it is given — no chunking, no internal concurrency — and returns positional results
    //  aligned with the input order; null marks an input this repository did not match. Repositories that
    //  cannot identify files throw NotSupportedException. Chunking and concurrency are the agent's job.
    Task<IReadOnlyList<Package?>> IdentifyBatchAsync(IEnumerable<ReadOnlyMemory<byte>> contents, CancellationToken cancellationToken = default);
    Task<Project> QueryAsync(ScopedProjectIdentifier id);

    Task<BatchResolveResult<ScopedProjectIdentifier, Project>> QueryBatchAsync(
        IEnumerable<ScopedProjectIdentifier> batch);

    Task<Package> ResolveAsync(ScopedPackageIdentifier id, Filter filter);

    Task<BatchResolveResult<ScopedPackageIdentifier, Package>> ResolveBatchAsync(
        IEnumerable<ScopedPackageIdentifier> batch,
        Filter filter);

    Task<string> ReadDescriptionAsync(ScopedProjectIdentifier id);
    Task<string> ReadChangelogAsync(ScopedPackageIdentifier id);
    Task<IPaginationHandle<Version>> InspectAsync(ScopedProjectIdentifier id, Filter filter);

    Task<PackageIdentifier> RecognizeAsync(Uri uri, CancellationToken cancellationToken = default);
}
