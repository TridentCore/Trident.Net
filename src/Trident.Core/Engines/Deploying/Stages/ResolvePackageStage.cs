using System.Collections.Concurrent;
using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;
using Trident.Core.Services;
using Trident.Core.Utilities;
using Trident.Abstractions;
using Trident.Abstractions.Repositories;
using Trident.Abstractions.Repositories.Resources;
using Trident.Abstractions.Utilities;

namespace Trident.Core.Engines.Deploying.Stages
{
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
                                               return new Purl(new(parsed.Label, parsed.Namespace, parsed.Pid),
                                                               parsed.Vid,
                                                               false);
                                           }

                                           throw new FormatException($"Package {x.Purl} is not a valid package");
                                       }));

            if (purls.Any())
            {
                ProgressStream.OnNext((0, purls.Count));

                var resolved = await agent
                                    .ResolveBatchAsync(purls.Select(x => (x.Identity.Label, x.Identity.Namespace,
                                                                          x.Identity.Pid, x.Vid)),
                                                       Filter.None with
                                                       {
                                                           Loader = loader,
                                                           Version = Context.Setup.Version
                                                       })
                                    .ConfigureAwait(false);

                foreach (var package in resolved)
                {
                    builder.AddParcel(package.Label,
                                      package.Namespace,
                                      package.ProjectId,
                                      package.ProjectId,
                                      Path.Combine(PathDef.Default.DirectoryOfBuild(Context.Key),
                                                   FileHelper.GetAssetFolderName(package.Kind),
                                                   package.FileName),
                                      package.Download,
                                      package.Sha1);
                }

                logger.LogDebug("Bulk resolved {} packages", resolved.Count);
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

        private record Purl(Identity Identity, string? Vid, bool IsPhantom);

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
}
