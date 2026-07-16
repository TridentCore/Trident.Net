using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;
using TridentCore.Abstractions;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Engines.Deploying.Stages;

public class SolidifyManifestStage(
    ILogger<SolidifyManifestStage> logger,
    IHttpClientFactory factory
) : StageBase
{
    private static readonly StringComparer PATH_COMPARER = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public Subject<(int Current, int Total)> ProgressStream { get; } = new();

    protected override async Task OnProcessAsync(CancellationToken token)
    {
        var manifest = Context.Manifest!;
        var buildDirectory = PathDef.Default.DirectoryOfBuild(Context.Key);
        var importDirectory = PathDef.Default.DirectoryOfImport(Context.Key);
        var persistDirectory = PathDef.Default.DirectoryOfPersist(Context.Key);

        var files = new List<object>();
        var projections = new Dictionary<string, ProjectionCandidate>(PATH_COMPARER);

        foreach (var fragile in manifest.FragileFiles)
        {
            if (FileHelper.IsInDirectory(fragile.TargetPath, buildDirectory))
            {
                UpsertProjection(
                    projections,
                    fragile.TargetPath,
                    ProjectionPriority.Package,
                    fragile,
                    $"package {fragile.TargetPath}"
                );
            }
            else
            {
                files.Add(fragile);
            }
        }

        foreach (var present in manifest.PresentFiles)
        {
            files.Add(present);
        }

        foreach (var persistent in manifest.PersistentFiles)
        {
            var priority = GetProjectionPriority(
                persistent,
                buildDirectory,
                importDirectory,
                persistDirectory
            );
            if (priority is { } actual)
            {
                UpsertProjection(
                    projections,
                    persistent.TargetPath,
                    actual,
                    persistent,
                    $"persistent {persistent.TargetPath}"
                );
            }
            else
            {
                files.Add(persistent);
            }
        }

        files.AddRange(projections.Values.Select(x => x.File));

        var total = files.Count + manifest.ExplosiveFiles.Count;
        logger.LogInformation("Created solidifying tasks of {}", total);

        var downloaded = 0;
        var semaphore = new SemaphoreSlim(Math.Max(Environment.ProcessorCount - 1, 1));
        var watch = Stopwatch.StartNew();
        var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        var entities = new ConcurrentBag<SymlinkPhotos.Entity>();

        ProgressStream.OnNext((0, total));
        var tasks = files
            .Select(async x =>
            {
                if (cancel.IsCancellationRequested)
                {
                    return;
                }

                var entered = false;
                try
                {
                    await semaphore.WaitAsync(cancel.Token).ConfigureAwait(false);
                    entered = true;
                    switch (x)
                    {
                        case EntityManifest.FragileFile fragile:
                            {
                                if (!FileHelper.VerifyModified(fragile.SourcePath, null, fragile.Hash))
                                {
                                    logger.LogDebug(
                                        "Starting download fragile file {src} from {url}",
                                        fragile.SourcePath,
                                        fragile.Url
                                    );
                                    var dir = Path.GetDirectoryName(fragile.SourcePath);
                                    if (dir != null && !Directory.Exists(dir))
                                    {
                                        Directory.CreateDirectory(dir);
                                    }

                                    using var client = factory.CreateClient(RepositoryAgent.CLIENT_NAME);
                                    await using var reader = await client
                                        .GetStreamAsync(fragile.Url, cancel.Token)
                                        .ConfigureAwait(false);
                                    await using var writer = new FileStream(
                                        fragile.SourcePath,
                                        FileMode.Create,
                                        FileAccess.Write,
                                        FileShare.Write
                                    );
                                    await reader
                                        .CopyToAsync(writer, cancel.Token)
                                        .ConfigureAwait(false);
                                    await writer.FlushAsync(cancel.Token).ConfigureAwait(false);
                                }

                                entities.Add(new(fragile.TargetPath, fragile.SourcePath, false));

                                break;
                            }
                        case EntityManifest.PresentFile present:
                            {
                                if (!FileHelper.VerifyModified(present.Path, null, present.Hash))
                                {
                                    var dir = Path.GetDirectoryName(present.Path);
                                    if (dir != null && !Directory.Exists(dir))
                                    {
                                        Directory.CreateDirectory(dir);
                                    }

                                    using var client = factory.CreateClient(RepositoryAgent.CLIENT_NAME);
                                    await using var reader = await client
                                        .GetStreamAsync(present.Url, cancel.Token)
                                        .ConfigureAwait(false);
                                    await using var writer = new FileStream(
                                        present.Path,
                                        FileMode.Create,
                                        FileAccess.Write,
                                        FileShare.Write
                                    );
                                    await reader
                                        .CopyToAsync(writer, cancel.Token)
                                        .ConfigureAwait(false);
                                    await writer.FlushAsync(cancel.Token).ConfigureAwait(false);
                                    if (
                                        present.IsExecutable
                                        && !OperatingSystem.IsWindows()
                                        && File.Exists(present.Path)
                                    )
                                    {
                                        var current = File.GetUnixFileMode(present.Path);
                                        File.SetUnixFileMode(
                                            present.Path,
                                            current
                                                | UnixFileMode.UserExecute
                                                | UnixFileMode.GroupExecute
                                                | UnixFileMode.OtherExecute
                                        );
                                    }
                                }

                                break;
                            }
                        case EntityManifest.PersistentFile persistent:
                            {
                                // 如果是虚文件（例如持久化文件功能），则在创建软链接前尝试确保目标文件不存在（最起码不是 Symlink）
                                // 不是虚文件时策略更简单，无则复制有则不管
                                if (persistent.IsPhantom)
                                {
                                    if (persistent.IsDirectory)
                                    {
                                        if (
                                            File.Exists(persistent.TargetPath)
                                            && File.ResolveLinkTarget(persistent.TargetPath, false)
                                                is null
                                        )
                                        {
                                            // 由于现在是目录模式，这时候只能丢弃文件，但是丢弃文件是不对的，所以直接报错！
                                            // TODO: 提供独特的异常包含更详细清晰的诊断信息并在前端展示
                                            throw new InvalidOperationException(
                                                $"Target {persistent.TargetPath} already exists as a normal file while trying to create a symlink from {persistent.SourcePath} as a directory"
                                            );
                                        }

                                        if (
                                            Directory.Exists(persistent.TargetPath)
                                            && Directory.ResolveLinkTarget(persistent.TargetPath, false)
                                                is null
                                        )
                                        {
                                            // 目标位置有个目录，先反向同步文件，替换同名（类似下面文件链接的原则），并创建链接
                                            var dirs = new Queue<string>();
                                            var toClean = new Stack<string>();
                                            toClean.Push(persistent.TargetPath);
                                            dirs.Enqueue(persistent.TargetPath);
                                            while (dirs.TryDequeue(out var src))
                                            {
                                                var dirRelative = Path.GetRelativePath(
                                                    persistent.TargetPath,
                                                    src
                                                );
                                                var dst = Path.Combine(
                                                    persistent.SourcePath,
                                                    dirRelative
                                                );
                                                if (!Directory.Exists(dst))
                                                {
                                                    Directory.CreateDirectory(dst);
                                                }

                                                foreach (var file in Directory.GetFiles(src))
                                                {
                                                    var target = Path.Combine(
                                                        dst,
                                                        Path.GetFileName(file)
                                                    );
                                                    logger.LogDebug(
                                                        "Backporting violating persistent file {src} to {dst}",
                                                        file,
                                                        target
                                                    );
                                                    File.Move(file, target, true);
                                                }

                                                foreach (var dir in Directory.GetDirectories(src))
                                                {
                                                    toClean.Push(dir);
                                                    dirs.Enqueue(dir);
                                                }
                                            }

                                            foreach (var dir in toClean)
                                            {
                                                // 关掉递归，以此确认上面的算法没问题
                                                Directory.Delete(dir, false);
                                            }
                                        }

                                        entities.Add(
                                            new(persistent.TargetPath, persistent.SourcePath, true)
                                        );
                                    }
                                    else
                                    {
                                        // 由于 Java 的落后性，有些模组更新文件并不是 Open-Overwrite，而是 Delete-Create
                                        // 导致软链接被删除而非覆盖
                                        // 遇到这种落后方式写入文件的会将 build/ 中的文件替换掉 live/ 的实现反向影响

                                        if (
                                            File.Exists(persistent.TargetPath)
                                            && File.ResolveLinkTarget(persistent.TargetPath, false)
                                                is null
                                        )
                                        {
                                            logger.LogDebug(
                                                "Backporting violating persistent file {src} to {dst}",
                                                persistent.TargetPath,
                                                persistent.SourcePath
                                            );
                                            File.Move(
                                                persistent.TargetPath,
                                                persistent.SourcePath,
                                                true
                                            );
                                        }

                                        entities.Add(
                                            new(persistent.TargetPath, persistent.SourcePath, false)
                                        );
                                    }
                                }
                                else
                                {
                                    if (IsSymbolicLink(persistent.TargetPath))
                                    {
                                        // NOTE: 遗留部署——旧版本把 import 经 live 软链接进 build。reset 是兜底，不做迁移。
                                        throw new InvalidOperationException(
                                            $"Legacy symlink-style import projection at {persistent.TargetPath}. "
                                            + "Reset the instance to clear it."
                                        );
                                    }

                                    if (persistent.IsDirectory)
                                    {
                                        // 收集源目录的文件，按存在原则复制到目标目录

                                        var dirs = new Queue<string>();
                                        dirs.Enqueue(persistent.SourcePath);
                                        while (dirs.TryDequeue(out var src))
                                        {
                                            var dirRelative = Path.GetRelativePath(
                                                persistent.SourcePath,
                                                src
                                            );
                                            var dst = Path.Combine(persistent.TargetPath, dirRelative);
                                            if (!Directory.Exists(dst))
                                            {
                                                Directory.CreateDirectory(dst);
                                            }

                                            foreach (var file in Directory.GetFiles(src))
                                            {
                                                var target = Path.Combine(dst, Path.GetFileName(file));
                                                if (!File.Exists(target))
                                                {
                                                    logger.LogDebug(
                                                        "Copying persistent file from {src} to {dst}",
                                                        persistent.SourcePath,
                                                        persistent.TargetPath
                                                    );
                                                    File.Copy(file, target);
                                                    File.SetLastWriteTimeUtc(
                                                        target,
                                                        File.GetLastWriteTimeUtc(file)
                                                    );
                                                }
                                            }

                                            foreach (var dir in Directory.GetDirectories(src))
                                            {
                                                dirs.Enqueue(dir);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        if (!File.Exists(persistent.TargetPath))
                                        {
                                            var dir = Path.GetDirectoryName(persistent.TargetPath);
                                            if (dir != null && !Directory.Exists(dir))
                                            {
                                                Directory.CreateDirectory(dir);
                                            }

                                            logger.LogDebug(
                                                "Copying persistent file from {src} to {dst}",
                                                persistent.SourcePath,
                                                persistent.TargetPath
                                            );
                                            File.Copy(persistent.SourcePath, persistent.TargetPath);
                                            File.SetLastWriteTimeUtc(
                                                persistent.TargetPath,
                                                File.GetLastWriteTimeUtc(persistent.SourcePath)
                                            );
                                        }
                                    }
                                }

                                break;
                            }
                    }

                    Interlocked.Increment(ref downloaded);
                    ProgressStream.OnNext((downloaded, total));
                }
                catch (OperationCanceledException) when (cancel.Token.IsCancellationRequested)
                {
                    // 源 Token 或级联 Token 取消触发
                    throw;
                }
                catch (Exception ex)
                {
                    await cancel.CancelAsync().ConfigureAwait(false);
                    logger.LogError(ex, "Failed to solidify {}", x);
                    throw;
                }
                finally
                {
                    if (entered)
                    {
                        semaphore.Release();
                    }
                }
            })
            .ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);

        foreach (var explosive in manifest.ExplosiveFiles)
        {
            logger.LogDebug(
                "Extracting {file} to {dir}",
                explosive.SourcePath,
                explosive.TargetDirectory
            );
            await using var zip = new ZipArchive(
                new FileStream(
                    explosive.SourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read
                ),
                ZipArchiveMode.Read,
                false
            );
            string? rootDir = null;
            var nested =
                explosive.Unwrap && ZipArchiveHelper.HasSingleRootDirectory(zip, out rootDir);
            foreach (var entry in zip.Entries)
            {
                if (entry.Length == 0)
                {
                    continue;
                }

                var path = Path.Combine(
                    explosive.TargetDirectory,
                    nested && !string.IsNullOrEmpty(rootDir)
                        ? entry.FullName[(rootDir.Length + 1)..]
                        : entry.FullName
                );
                // Skip the empty file and directory(Length == 0 as well)
                if (
                    !File.Exists(path)
                    || File.GetLastWriteTimeUtc(path) < entry.LastWriteTime.UtcDateTime
                )
                {
                    var dir = Path.GetDirectoryName(path);
                    if (dir != null && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    await using var reader = entry.Open();
                    await using (
                        var writer = new FileStream(
                            path,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.Write
                        )
                    )
                    {
                        await reader.CopyToAsync(writer, cancel.Token).ConfigureAwait(false);
                        await writer.FlushAsync(cancel.Token).ConfigureAwait(false);
                    }

                    File.SetLastWriteTimeUtc(path, entry.LastWriteTime.UtcDateTime);
                }
            }

            ProgressStream.OnNext((++downloaded, total));
        }

        SymlinkPhotos.Apply(buildDirectory, entities.ToArray());

        // 生成 allowed_symlinks.txt
        if (!Path.Exists(buildDirectory))
        {
            Directory.CreateDirectory(buildDirectory);
        }

        await File.WriteAllTextAsync(
                Path.Combine(buildDirectory, "allowed_symlinks.txt"),
                $"""
                [prefix]{PathDef.Default.CachePackageDirectory}
                [prefix]{PathDef.Default.DirectoryOfPersist(Context.Key)}
                """,
                cancel.Token
            )
            .ConfigureAwait(false);

        watch.Stop();
        logger.LogInformation("Solidifying finished in {ms}ms", watch.ElapsedMilliseconds);
    }

    public override void Dispose()
    {
        base.Dispose();
        ProgressStream.Dispose();
    }

    private static bool IsSymbolicLink(string path)
    {
        try
        {
            if (File.ResolveLinkTarget(path, false) is not null)
            {
                return true;
            }
        }
        catch (IOException) { }

        try
        {
            if (Directory.ResolveLinkTarget(path, false) is not null)
            {
                return true;
            }
        }
        catch (IOException) { }

        return false;
    }

    private ProjectionPriority? GetProjectionPriority(
        EntityManifest.PersistentFile persistent,
        string buildDir,
        string importDir,
        string persistDir
    )
    {
        if (!FileHelper.IsInDirectory(persistent.TargetPath, buildDir))
        {
            return null;
        }

        if (FileHelper.IsInDirectory(persistent.SourcePath, persistDir))
        {
            return ProjectionPriority.Persist;
        }

        if (FileHelper.IsInDirectory(persistent.SourcePath, importDir))
        {
            return ProjectionPriority.Import;
        }

        return null;
    }

    private void UpsertProjection(
        IDictionary<string, ProjectionCandidate> projections,
        string targetPath,
        ProjectionPriority priority,
        object file,
        string description
    )
    {
        if (projections.TryGetValue(targetPath, out var existing))
        {
            if (priority > existing.Priority)
            {
                logger.LogDebug(
                    "Projection {next} overrides {current} at {target}",
                    description,
                    existing.Description,
                    targetPath
                );
                projections[targetPath] = new(file, priority, description);
            }
            else
            {
                logger.LogDebug(
                    "Projection {current} keeps {target} over {skipped}",
                    existing.Description,
                    targetPath,
                    description
                );
            }

            return;
        }

        projections[targetPath] = new(file, priority, description);
    }

    private enum ProjectionPriority
    {
        Package = 0,
        Import = 1,
        Persist = 2,
    }

    private record ProjectionCandidate(
        object File,
        ProjectionPriority Priority,
        string Description
    );
}
