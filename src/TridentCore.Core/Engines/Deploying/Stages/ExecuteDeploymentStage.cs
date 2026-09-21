using System.Reactive.Subjects;
using TridentCore.Abstractions;
using TridentCore.Core.Exceptions;
using TridentCore.Core.Extensions;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Engines.Deploying.Stages;

public class ExecuteDeploymentStage(IHttpClientFactory factory) : StageBase
{
    public Subject<(int Current, int Total)> ProgressStream { get; } = new();

    protected override async Task OnProcessAsync(CancellationToken token)
    {
        var plan = Context.Plan!;
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

        var build = PathDef.Default.DirectoryOfBuild(Context.Key);
        var persist = PathDef.Default.DirectoryOfPersist(Context.Key);
        foreach (var operation in plan.Operations)
        {
            token.ThrowIfCancellationRequested();
            switch (operation)
            {
                case DeploymentPlan.EnsureImportFile copy:
                    EnsureImportFile(build, copy.Source, copy.Target);
                    break;
                case DeploymentPlan.RemoveBuildFile remove:
                    RemoveBuildFile(build, remove.Path);
                    break;
                case DeploymentPlan.MoveToPersist move:
                    MoveToPersist(build, persist, move.Source, move.Target);
                    break;
                case DeploymentPlan.BackportPersistFile backport:
                    BackportPersistFile(build, persist, backport);
                    break;
                case DeploymentPlan.RemoveLink remove:
                    RemoveLink(build, remove.Path);
                    break;
                case DeploymentPlan.EnsureLink link:
                    EnsureLink(build, link);
                    break;
                case DeploymentPlan.MakeExecutable executable:
                    MakeExecutable(executable.Path);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown deployment operation: {operation}");
            }
            ProgressStream.OnNext((++completed, total));
        }

        // 计划（DeploymentPlanner）、求差（DeploymentDiffer）、应用（上面的下载与操作循环）三段是一个整体，
        // 前两段抽出为独立类，供 InstanceStateService 复用以判断实例就绪。
        // 以下的 natives 提取、allowed_symlinks 写入与清单提交是收尾，属于另一个整体，
        // 不属于前三段，刻意保持内联而不做成 Operation。
        var natives = Context.Lock.Artifact!.AllLibraries().Where(x => x.IsNative)
            .Select(x => new NativeHelper.Archive(x.FilePath(Context.Key), false, x.Exclude)).ToArray();
        var nativesDirectory = PathDef.Default.DirectoryOfNatives(Context.Key);
        DeploymentFileHelper.DeleteLink(nativesDirectory);
        await NativeHelper.ExtractAsync(nativesDirectory, natives, token).ConfigureAwait(false);

        await WriteAllowedSymlinksAsync(build, persist, token).ConfigureAwait(false);
        if (plan.NeedsManifestCommit)
            await ProjectionManifestHelper.WriteAsync(Context.Key, plan.ImportManifest, plan.PersistManifest, token)
                .ConfigureAwait(false);
    }

    private static void EnsureImportFile(string build, string source, string target)
    {
        DeploymentFileHelper.EnsureRealParent(target, build);
        DeploymentFileHelper.DeleteLink(target);
        if (Directory.Exists(target) && !DeploymentFileHelper.DeleteDirectoryTreeIfEmptyOrLinks(target, build))
            throw BuildArtifactConflictException.Occupied(target);
        if (File.Exists(target)) return;
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, temporary);
            File.SetLastWriteTimeUtc(temporary, File.GetLastWriteTimeUtc(source));
            File.Move(temporary, target, false);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static void RemoveBuildFile(string build, string path)
    {
        if (DeploymentFileHelper.HasLinkAtOrAbove(path, build)) return;
        if (File.Exists(path)) File.Delete(path);
        DeploymentFileHelper.TrimEmptyParents(build, Path.GetDirectoryName(path));
    }

    private static void MoveToPersist(string build, string persist, string source, string target)
    {
        if (DeploymentFileHelper.HasLinkAtOrAbove(source, build)) return;
        if (!File.Exists(source))
        {
            DeploymentFileHelper.TrimEmptyParents(build, Path.GetDirectoryName(source));
            return;
        }
        if (Directory.Exists(source)) throw BuildArtifactConflictException.Occupied(source);
        DeploymentFileHelper.EnsureRealParent(target, persist);
        if (Path.Exists(target)) throw BuildArtifactConflictException.Occupied(source);
        File.Move(source, target);
        DeploymentFileHelper.TrimEmptyParents(build, Path.GetDirectoryName(source));
    }

    private static void BackportPersistFile(
        string build,
        string persist,
        DeploymentPlan.BackportPersistFile operation)
    {
        if (DeploymentFileHelper.HasLinkAtOrAbove(operation.Source, build)) return;
        if (!File.Exists(operation.Source))
        {
            DeploymentFileHelper.TrimEmptyParents(build, Path.GetDirectoryName(operation.Source));
            return;
        }
        if (Directory.Exists(operation.Source)) throw BuildArtifactConflictException.Occupied(operation.Source);
        DeploymentFileHelper.EnsureRealParent(operation.Target, persist);
        if (DeploymentFileHelper.LinkTarget(operation.Target) is not null || !File.Exists(operation.Target))
            throw BuildArtifactConflictException.Occupied(operation.Target);
        if (File.GetLastWriteTimeUtc(operation.Target).Ticks != operation.ExpectedTargetLastWriteTimeUtcTicks)
            throw BuildArtifactConflictException.Occupied(operation.Target);
        File.Move(operation.Source, operation.Target, true);
        DeploymentFileHelper.TrimEmptyParents(build, Path.GetDirectoryName(operation.Source));
    }

    private static void RemoveLink(string build, string path)
    {
        if (!DeploymentFileHelper.DeleteLink(path)) return;
        DeploymentFileHelper.TrimEmptyParents(build, Path.GetDirectoryName(path));
    }

    private static void EnsureLink(string build, DeploymentPlan.EnsureLink operation)
    {
        DeploymentFileHelper.EnsureRealParent(operation.Path, build);
        DeploymentFileHelper.DeleteLink(operation.Path);
        if (File.Exists(operation.Path)) throw BuildArtifactConflictException.Occupied(operation.Path);
        if (Directory.Exists(operation.Path)
            && !DeploymentFileHelper.DeleteDirectoryTreeIfEmptyOrLinks(operation.Path, build)) throw BuildArtifactConflictException.Occupied(operation.Path);
        if (operation.Directory) Directory.CreateSymbolicLink(operation.Path, operation.Target);
        else File.CreateSymbolicLink(operation.Path, operation.Target);
    }

    private static async Task WriteAllowedSymlinksAsync(string build, string persist, CancellationToken token)
    {
        Directory.CreateDirectory(build);
        var path = Path.Combine(build, PathDef.ALLOWED_SYMLINKS_FILE_NAME);
        DeploymentFileHelper.DeleteLink(path);
        if (Directory.Exists(path)) throw BuildArtifactConflictException.Occupied(path);
        var content = $"[prefix]{PathDef.Default.CachePackageDirectory}\n[prefix]{persist}";
        if (!File.Exists(path) || await File.ReadAllTextAsync(path, token).ConfigureAwait(false) != content)
            await File.WriteAllTextAsync(path, content, token).ConfigureAwait(false);
    }

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
