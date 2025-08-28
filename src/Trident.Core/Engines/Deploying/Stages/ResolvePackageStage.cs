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
            var flatten = new Dictionary<Identity, Version>();

            ProgressStream.OnNext((0, purls.Count));

            while (purls.Any())
            {
                var resolved = await agent
                                    .ResolveBatchAsync(purls.Select(x => (x.Identity.Label, x.Identity.Namespace,
                                                                          x.Identity.Pid, x.Vid)),
                                                       Filter.None with
                                                       {
                                                           Loader = loader, Version = Context.Setup.Version
                                                       })
                                    .ConfigureAwait(false);

                logger.LogDebug("Bulk resolved {} packages", resolved.Count);
                purls.Clear();

                // 依赖解析直接不要了，本来就没法用，留着也是累赘

                // TODO: 如果批量解析存在失败那就按需重试几次

                ProgressStream.OnNext((flatten.Count, purls.Count + flatten.Count));
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            foreach (var (key, value) in flatten)
            {
                builder.AddParcel(key.Label,
                                  key.Namespace,
                                  key.Pid,
                                  value.Vid,
                                  Path.Combine(PathDef.Default.DirectoryOfBuild(Context.Key),
                                               FileHelper.GetAssetFolderName(value.Kind),
                                               value.FileName),
                                  value.Download,
                                  value.Sha1);
            }

            Context.IsPackageResolved = true;
            return;
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
