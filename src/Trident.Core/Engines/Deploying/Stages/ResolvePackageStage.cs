using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;
using Trident.Abstractions;
using Trident.Abstractions.FileModels;
using Trident.Abstractions.Repositories;
using Trident.Abstractions.Repositories.Resources;
using Trident.Abstractions.Utilities;
using Trident.Core.Services;
using Trident.Core.Utilities;
using Trident.Purl;

namespace Trident.Core.Engines.Deploying.Stages;

public class ResolvePackageStage(ILogger<ResolvePackageStage> logger, RepositoryAgent agent) : StageBase
{
    public Subject<(int, int)> ProgressStream { get; } = new();

    protected override async Task OnProcessAsync(CancellationToken token)
    {
        var builder = Context.ArtifactBuilder!;

        string? loader = null;
        if (Context.Setup.Loader != null)
        {
            if (LoaderHelper.TryParse(Context.Setup.Loader, out var result))
            {
                loader = result.Identity;
            }
            else
            {
                throw new FormatException($"{Context.Setup.Loader} is not well formatted loader string");
            }
        }

        var purls = new List<Purl>(Context
                                  .Setup.Packages.Where(x => x.Enabled)
                                  .Select(x =>
                                   {
                                       if (PackageHelper.TryParse(x.Purl, out var parsed))
                                       {
                                           return new Purl(x,
                                                           new(parsed.Label, parsed.Namespace, parsed.Pid),
                                                           parsed.Vid,
                                                           false);
                                       }

                                       throw new FormatException($"Package {x.Purl} is not a valid package");
                                   }));

        if (purls.Count != 0)
        {
            ProgressStream.OnNext((0, purls.Count));

            var index = purls
                       .Select(x => (Key: new PackageIdentifier(x.Id.Label, x.Id.Namespace, x.Id.Pid, x.Vid),
                                     Value: x.Origin))
                       .ToDictionary(x => x.Key, x => x.Value);

            var resolved = await agent
                                .ResolveBatchAsync(index.Keys,
                                                   Filter.None with
                                                   {
                                                       Loader = loader,
                                                       Version = Context.Setup.Version
                                                   })
                                .ConfigureAwait(false);

            var enabledRules = Context.Setup.Rules.Where(x => x.Enabled).ToList();

            foreach (var (id, package) in resolved)
            {
                var entry = index[id];
                var result = RuleHelper.Evaluate(new RuleHelper.Input(entry, package), enabledRules);

                if (result is { Matched: true, EffectiveRule: { } effectiveRule })
                {
                    logger.LogDebug("Rule {{ {skipping}, {solidifying}, {destination} }} applied to {purl}",
                                    entry.Purl,
                                    effectiveRule.Skipping,
                                    effectiveRule.Solidifying,
                                    effectiveRule.Destination ?? "<default>");
                    if (effectiveRule.Skipping)
                    {
                        continue;
                    }

                    var target = Path.Combine(PathDef.Default.DirectoryOfBuild(Context.Key),
                                              FileHelper.GetAssetFolderName(package.Kind),
                                              package.FileName);
                    if (effectiveRule.Destination is not null)
                    {
                        target = Path.Combine(PathDef.Default.DirectoryOfBuild(Context.Key),
                                              effectiveRule.Destination,
                                              package.FileName);
                        if (!FileHelper.IsInDirectory(target, PathDef.Default.DirectoryOfBuild(Context.Key)))
                        {
                            throw new InvalidOperationException($"Destination {target} is outside of build directory");
                        }
                    }

                    builder.AddParcel(package.Label,
                                      package.Namespace,
                                      package.ProjectId,
                                      package.VersionId,
                                      target,
                                      package.Download,
                                      package.Sha1,
                                      effectiveRule.Solidifying);
                }
                else
                {
                    builder.AddParcel(package.Label,
                                      package.Namespace,
                                      package.ProjectId,
                                      package.VersionId,
                                      Path.Combine(PathDef.Default.DirectoryOfBuild(Context.Key),
                                                   FileHelper.GetAssetFolderName(package.Kind),
                                                   package.FileName),
                                      package.Download,
                                      package.Sha1);
                }
            }

            logger.LogDebug("Batch resolved {} packages", resolved.Count);
            purls.Clear();

            // 依赖解析直接不要了，本来就没法用，留着也是累赘

            ProgressStream.OnNext((resolved.Count, resolved.Count + purls.Count));
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        Context.IsPackageResolved = true;
    }

    public override void Dispose()
    {
        base.Dispose();
        ProgressStream.Dispose();
    }

    #region Nested type: Identity

    private record Identity(string Label, string? Namespace, string Pid);

    #endregion

    #region Nested type: Purl

    private record Purl(Profile.Rice.Entry Origin, Identity Id, string? Vid, bool IsPhantom);

    #endregion

    #region Nested type: Version

    private record Version(
        string Vid,
        ResourceKind Kind,
        DateTimeOffset ReleasedAt,
        string FileName,
        string? Sha1,
        Uri Download,
        bool IsReliable = true);

    #endregion
}
