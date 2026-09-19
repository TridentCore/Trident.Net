using System.Reactive.Subjects;
using TridentCore.Abstractions;
using TridentCore.Core.Extensions;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Engines.Deploying.Stages;

public class SolidifyManifestStage(IHttpClientFactory factory) : StageBase
{
    public Subject<(int Current, int Total)> ProgressStream { get; } = new();

    protected override async Task OnProcessAsync(CancellationToken token)
    {
        var plan = Context.Manifest!;
        var total = plan.Downloads.Count + plan.Operations.Count;
        var completed = 0;
        ProgressStream.OnNext((0, total));
        using var client = factory.CreateClient(RepositoryAgent.CLIENT_NAME);
        await Parallel.ForEachAsync(plan.Downloads, new ParallelOptions
        {
            CancellationToken = token,
            MaxDegreeOfParallelism = Math.Max(Environment.ProcessorCount - 1, 1)
        }, async (download, ct) =>
        {
            await DownloadHelper.DownloadAsync(client, download.Url, download.Path, download.Hash, ct).ConfigureAwait(false);
            if (download.Executable) MakeExecutable(download.Path);
            lock (ProgressStream) ProgressStream.OnNext((++completed, total));
        }).ConfigureAwait(false);

        foreach (var operation in plan.Operations)
        {
            token.ThrowIfCancellationRequested();
            switch (operation)
            {
                case DeploymentPlan.CreateDirectory directory:
                    Directory.CreateDirectory(directory.Path);
                    break;
                case DeploymentPlan.Copy copy:
                    EnsureParent(copy.Target);
                    File.Copy(copy.Source, copy.Target);
                    File.SetLastWriteTimeUtc(copy.Target, File.GetLastWriteTimeUtc(copy.Source));
                    break;
                case DeploymentPlan.Move move:
                    EnsureParent(move.Target);
                    File.Move(move.Source, move.Target, true);
                    break;
                case DeploymentPlan.RemoveLink remove:
                    if (FilePlanningHelper.LinkTarget(remove.Path) is null)
                        throw new IOException($"The planned symbolic link has changed: {remove.Path}");
                    if (remove.Directory) Directory.Delete(remove.Path, false);
                    else File.Delete(remove.Path);
                    break;
                case DeploymentPlan.RemoveDirectory remove:
                    Directory.Delete(remove.Path, false);
                    break;
                case DeploymentPlan.Link link:
                    EnsureParent(link.Path);
                    if (link.Directory) Directory.CreateSymbolicLink(link.Path, link.Target);
                    else File.CreateSymbolicLink(link.Path, link.Target);
                    break;
                case DeploymentPlan.MakeExecutable executable:
                    MakeExecutable(executable.Path);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown deployment operation: {operation}");
            }
            ProgressStream.OnNext((++completed, total));
        }

        var natives = Context.Lock.Artifact!.AllLibraries().Where(x => x.IsNative)
            .Select(x => new NativeHelper.Archive(x.FilePath(Context.Key), false, x.Exclude)).ToArray();
        await NativeHelper.ExtractAsync(PathDef.Default.DirectoryOfNatives(Context.Key), natives, token).ConfigureAwait(false);
        var build = PathDef.Default.DirectoryOfBuild(Context.Key);
        Directory.CreateDirectory(build);
        var path = Path.Combine(build, "allowed_symlinks.txt");
        var content = $"[prefix]{PathDef.Default.CachePackageDirectory}\n[prefix]{PathDef.Default.DirectoryOfPersist(Context.Key)}";
        if (!File.Exists(path) || await File.ReadAllTextAsync(path, token).ConfigureAwait(false) != content)
            await File.WriteAllTextAsync(path, content, token).ConfigureAwait(false);
    }

    private static void EnsureParent(string path) => Directory.CreateDirectory(Path.GetDirectoryName(path)!);

    private static void MakeExecutable(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    public override void Dispose()
    {
        base.Dispose();
        ProgressStream.Dispose();
    }
}
