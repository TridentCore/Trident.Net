using Microsoft.Extensions.Logging;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;
using TridentCore.Purl;

namespace TridentCore.Core.Engines.Deploying;

public class PackagePlanner(ILogger<PackagePlanner> logger, RepositoryAgent agent)
{
    public async IAsyncEnumerable<PackagePlan> PlanAsync(
        IReadOnlyList<Profile.Rice.Entry> packages,
        PackagePlannerContext context)
    {
        var purls = new List<Purl>(packages.Select(x =>
        {
            if (PackageHelper.TryParse(x.Purl, out var parsed))
            {
                return new Purl(x, new(parsed.Label, parsed.Namespace, parsed.Pid), parsed.Vid);
            }

            throw new FormatException($"Package {x.Purl} is not a valid package");
        }));

        if (purls.Count > 0)
        {
            var index = purls
                       .Select(x => (Key: new PackageIdentifier(x.Id.Label, x.Id.Namespace, x.Id.Pid, x.Vid),
                                     Value: x.Origin))
                       .ToDictionary(x => x.Key, x => x.Value);

            var resolved = await agent.ResolveBatchAsync(index.Keys, context.Filter).ConfigureAwait(false);

            foreach (var (id, package) in resolved)
            {
                var entry = index[id];
                var result = RuleHelper.Evaluate(new RuleHelper.Input(entry, package), context.Rules);

                if (result is { Matched: true, EffectiveRule: { } effectiveRule })
                {
                    logger.LogDebug("Rule {{ {skipping}, {destination} }} applied to {purl}",
                                    effectiveRule.Skipping,
                                    effectiveRule.Destination ?? "<default>",
                                    entry.Purl);

                    var fileName = effectiveRule.Normalizing
                                       ? string.Concat(FileHelper.Sanitize(package.ProjectName),
                                                       Path.GetExtension(package.FileName))
                                       : package.FileName;
                    var target = effectiveRule.Destination is not null
                                     ? Path.Combine(effectiveRule.Destination, fileName)
                                     : Path.Combine(FileHelper.GetAssetFolderName(package.Kind), fileName);

                    yield return new(package.Label,
                                     package.Namespace,
                                     package.ProjectId,
                                     package.VersionId,
                                     target,
                                     package.Download,
                                     package.Sha1)
                    { IsSkipping = effectiveRule.Skipping, };
                }
                else
                {
                    yield return new(package.Label,
                                     package.Namespace,
                                     package.ProjectId,
                                     package.VersionId,
                                     Path.Combine(FileHelper.GetAssetFolderName(package.Kind), package.FileName),
                                     package.Download,
                                     package.Sha1);
                }
            }
        }
    }

    #region Nested type: Purl

    private record Purl(Profile.Rice.Entry Origin, Identity Id, string? Vid);

    #endregion


    #region Nested type: Identity

    private record Identity(string Label, string? Namespace, string Pid);

    #endregion
}
