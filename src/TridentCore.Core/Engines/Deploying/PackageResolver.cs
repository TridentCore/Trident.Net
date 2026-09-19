using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Repositories;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Services;
using TridentCore.Pref;

namespace TridentCore.Core.Engines.Deploying;

public class PackageResolver(RepositoryAgent agent)
{
    public async Task<IReadOnlyList<(Profile.Rice.Entry Entry, Package Package)>> ResolveAsync(
        IReadOnlyList<Profile.Rice.Entry> packages,
        Filter filter)
    {
        var index = new List<(PackageIdentifier Key, Profile.Rice.Entry Origin)>();
        foreach (var entry in packages)
        {
            if (!PackageHelper.TryParse(entry.Pref, out var parsed))
            {
                throw new FormatException($"Package {entry.Pref} is not a valid package");
            }

            index.Add((new(parsed.Repository, parsed.Namespace, parsed.Identity, parsed.Version), entry));
        }

        if (index.Count == 0)
        {
            return [];
        }

        var resolved = await agent.ResolveBatchAsync(index.Select(x => x.Key).Distinct(), filter).ConfigureAwait(false);

        resolved.ThrowIfFailures();

        // NOTE: 同项目同版本现可来自不同源（SyncPackages 以 (project, source) 为键）；
        //  把一次解析扇出给共享该键的每个条目。
        var byKey = index.ToLookup(x => x.Key, x => x.Origin);
        return [.. resolved.Successful.SelectMany(x => byKey[x.Key].Select(origin => (origin, x.Value)))];
    }

}
