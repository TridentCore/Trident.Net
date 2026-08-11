using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Pref;
using Version = TridentCore.Abstractions.Repositories.Resources.Version;

namespace TridentCore.Abstractions.Repositories;

public interface IRepository
{
    // NOTE: 隐藏仓库仍注册、可按 label 解析，但从 RepositoryAgent.Labels 排除，
    //  永不现身浏览/搜索/市场列表。
    bool IsHidden => false;
    Task<RepositoryStatus> CheckStatusAsync();
    Task<IPaginationHandle<Exhibit>> SearchAsync(string query, Filter filter);
    Task<Package> IdentifyAsync(ReadOnlyMemory<byte> content);

    // NOTE: batch counterpart of IdentifyAsync. The repository queries its native batch endpoint with
    //  exactly what it is given — no chunking, no internal concurrency — and returns positional results
    //  aligned with the input order; null marks an input this repository did not match. Repositories that
    //  cannot identify files throw NotSupportedException. Chunking and concurrency are the agent's job.
    Task<IReadOnlyList<Package?>> IdentifyBatchAsync(
        IEnumerable<ReadOnlyMemory<byte>> contents,
        CancellationToken cancellationToken = default);

    Task<Project> QueryAsync(ScopedProjectIdentifier id);

    Task<BatchResult<ScopedProjectIdentifier, Project>> QueryBatchAsync(
        IEnumerable<ScopedProjectIdentifier> batch);

    Task<Package> ResolveAsync(ScopedPackageIdentifier id, Filter filter);

    Task<BatchResult<ScopedPackageIdentifier, Package>> ResolveBatchAsync(
        IEnumerable<ScopedPackageIdentifier> batch,
        Filter filter);

    Task<string> ReadDescriptionAsync(ScopedProjectIdentifier id);
    Task<string> ReadChangelogAsync(ScopedPackageIdentifier id);
    Task<IPaginationHandle<Version>> InspectAsync(ScopedProjectIdentifier id, Filter filter);

    Task<PackageIdentifier> RecognizeAsync(Uri uri, CancellationToken cancellationToken = default);

    // NOTE: batch counterpart of RecognizeAsync. The result is total — every input uri lands in
    //  exactly one of Successful or Failed. A ResourceNotFoundException in Failed is the
    //  repository's "not my territory" signal that the agent re-probes at the next repository;
    //  any other exception means the repository claimed the uri but failed to resolve it.
    Task<BatchResult<Uri, PackageIdentifier>> RecognizeBatchAsync(
        IEnumerable<Uri> uris,
        CancellationToken cancellationToken = default);
}
