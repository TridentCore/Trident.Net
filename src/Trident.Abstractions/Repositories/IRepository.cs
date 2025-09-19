using Trident.Abstractions.Repositories.Resources;
using Version = Trident.Abstractions.Repositories.Resources.Version;

namespace Trident.Abstractions.Repositories;

public interface IRepository
{
    Task<RepositoryStatus> CheckStatusAsync();
    Task<IPaginationHandle<Exhibit>> SearchAsync(string query, Filter filter);
    Task<Package> IdentifyAsync(ReadOnlyMemory<byte> content);
    Task<Project> QueryAsync(string? ns, string pid);
    Task<IReadOnlyList<Project>> QueryBatchAsync(IEnumerable<(string?, string pid)> batch);
    Task<Package> ResolveAsync(string? ns, string pid, string? vid, Filter filter);

    Task<IReadOnlyList<Package>> ResolveBatchAsync(
        IEnumerable<(string? ns, string pid, string? vid)> batch,
        Filter filter);

    Task<string> ReadDescriptionAsync(string? ns, string pid);
    Task<string> ReadChangelogAsync(string? ns, string pid, string vid);
    Task<IPaginationHandle<Version>> InspectAsync(string? ns, string pid, Filter filter);
}
