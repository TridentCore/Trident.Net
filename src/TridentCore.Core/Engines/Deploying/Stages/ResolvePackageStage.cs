using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;
using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Repositories;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;
using TridentCore.Purl;

namespace TridentCore.Core.Engines.Deploying.Stages;

public class ResolvePackageStage(
    PackagePlanner planner) : StageBase
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

        var filter = new Filter(Context.Setup.Version, loader, null);
        var packages = Context.Setup.Packages.Where(x => x.Enabled).ToList();
        var rules = Context.Setup.Rules.Where(x => x.Enabled).ToList();

        var context = new PackagePlannerContext(rules, filter);

        ProgressStream.OnNext((0, packages.Count));

        await foreach (var plan in planner.PlanAsync(packages, context).WithCancellation(token).ConfigureAwait(false))
        {
            builder.AddParcel(plan.Label,
                              plan.Namespace,
                              plan.ProjectId,
                              plan.VersionId,
                              Path.Combine(PathDef.Default.DirectoryOfBuild(Context.Key), plan.TargetPath),
                              plan.Url,
                              plan.Sha1);
        }

        ProgressStream.OnNext((packages.Count, packages.Count));


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
}
