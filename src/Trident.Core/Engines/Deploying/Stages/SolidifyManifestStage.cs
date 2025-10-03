using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reactive.Subjects;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Trident.Abstractions;

namespace Trident.Core.Engines.Deploying.Stages;

public class SolidifyManifestStage(ILogger<SolidifyManifestStage> logger, IHttpClientFactory factory) : StageBase
{
    public Subject<(int, int)> ProgressStream { get; } = new();

    protected override async Task OnProcessAsync(CancellationToken token)
    {
        var manifest = Context.Manifest!;

        var files = new List<object>();

        foreach (var fragile in manifest.FragileFiles)
        {
            files.Add(fragile);
        }

        foreach (var present in manifest.PresentFiles)
        {
            files.Add(present);
        }

        foreach (var persistent in manifest.PersistentFiles)
        {
            files.Add(persistent);
        }

        logger.LogInformation("Created solidifying tasks of {}", files.Count + manifest.ExplosiveFiles.Count);

        var buildDir = PathDef.Default.DirectoryOfBuild(Context.Key);

        var downloaded = 0;
        var semaphore = new SemaphoreSlim(Math.Max(Environment.ProcessorCount - 1, 1));
        var watch = Stopwatch.StartNew();
        var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        var entities = new List<Snapshot.Entity>();

        ProgressStream.OnNext((downloaded, files.Count));
        var tasks = files
                   .Select(async x =>
                    {
                        if (cancel.IsCancellationRequested)
                        {
                            return;
                        }

                        try
                        {
                            await semaphore.WaitAsync(cancel.Token).ConfigureAwait(false);
                            switch (x)
                            {
                                case EntityManifest.FragileFile fragile:
                                {
                                    if (!Verify(fragile.SourcePath, fragile.Hash))
                                    {
                                        logger.LogDebug("Starting download fragile file {src} from {url}",
                                                        fragile.SourcePath,
                                                        fragile.Url);
                                        var dir = Path.GetDirectoryName(fragile.SourcePath);
                                        if (dir != null && !Directory.Exists(dir))
                                        {
                                            Directory.CreateDirectory(dir);
                                        }

                                        using var client = factory.CreateClient();
                                        await using var reader = await client
                                                                      .GetStreamAsync(fragile.Url, cancel.Token)
                                                                      .ConfigureAwait(false);
                                        await using var writer = new FileStream(fragile.SourcePath,
                                                                                    FileMode.Create,
                                                                                    FileAccess.Write,
                                                                                    FileShare.Write);
                                        await reader.CopyToAsync(writer, cancel.Token).ConfigureAwait(false);
                                        await writer.FlushAsync(cancel.Token).ConfigureAwait(false);
                                    }

                                    entities.Add(new(fragile.TargetPath, fragile.SourcePath, false));

                                    break;
                                }
                                case EntityManifest.PresentFile present:
                                {
                                    if (!Verify(present.Path, present.Hash))
                                    {
                                        var dir = Path.GetDirectoryName(present.Path);
                                        if (dir != null && !Directory.Exists(dir))
                                        {
                                            Directory.CreateDirectory(dir);
                                        }

                                        using var client = factory.CreateClient();
                                        await using var reader = await client
                                                                      .GetStreamAsync(present.Url, cancel.Token)
                                                                      .ConfigureAwait(false);
                                        await using var writer = new FileStream(present.Path,
                                                                                    FileMode.Create,
                                                                                    FileAccess.Write,
                                                                                    FileShare.Write);
                                        await reader.CopyToAsync(writer, cancel.Token).ConfigureAwait(false);
                                        await writer.FlushAsync(cancel.Token).ConfigureAwait(false);
                                    }

                                    break;
                                }
                                case EntityManifest.PersistentFile persistent:
                                {
                                    // 如果是虚文件（例如持久化文件功能），则在创建软链接前尝试确保目标文件不存在（最起码不是 Symlink）
                                    // 不是虚文件时策略更简单，无则复制有则不管
                                    if (persistent.IsPhantom)
                                    {
                                        // 由于 Java 的落后性，有些模组更新文件并不是 Open-Overwrite，而是 Delete-Create
                                        // 导致软链接被删除而非覆盖
                                        // 遇到这种落后方式写入文件的会将 build/ 中的文件替换掉 live/ 的实现反向影响

                                        if (File.Exists(persistent.TargetPath)
                                         && File.ResolveLinkTarget(persistent.TargetPath, false) is null)
                                        {
                                            File.Move(persistent.TargetPath, persistent.SourcePath, true);
                                        }

                                        entities.Add(new(persistent.TargetPath,
                                                         persistent.SourcePath,
                                                         persistent.IsDirectory));
                                    }
                                    else if (!File.Exists(persistent.TargetPath))
                                    {
                                        var dir = Path.GetDirectoryName(persistent.TargetPath);
                                        if (dir != null && !Directory.Exists(dir))
                                        {
                                            Directory.CreateDirectory(dir);
                                        }

                                        logger.LogDebug("Copying persistent file from {src} to {dst}",
                                                        persistent.SourcePath,
                                                        persistent.TargetPath);
                                        File.Copy(persistent.SourcePath, persistent.TargetPath);
                                    }

                                    break;
                                }
                            }

                            Interlocked.Increment(ref downloaded);
                            ProgressStream.OnNext((downloaded, files.Count + manifest.ExplosiveFiles.Count));
                        }
                        catch (OperationCanceledException) { }
                        catch (Exception ex)
                        {
                            await cancel.CancelAsync().ConfigureAwait(false);
                            logger.LogError(ex, "Failed to solidify {}", x);
                            throw;
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    })
                   .ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);

        if (cancel.IsCancellationRequested)
        {
            return;
        }

        foreach (var explosive in manifest.ExplosiveFiles)
        {
            // if (Directory.Exists(explosive.TargetDirectory) && explosive.IsDestructive)
            // {
            //     // 只有 build 目录里才具有摧毁性
            //     var full = Path.GetFullPath(explosive.TargetDirectory);
            //     if (full.StartsWith(fullBuildDir))
            //     {
            //         logger.LogDebug("Destroying {}", full);
            //         Directory.Delete(explosive.TargetDirectory, true);
            //     }
            // }
            //
            // if (!Directory.Exists(explosive.TargetDirectory))
            //     Directory.CreateDirectory(explosive.TargetDirectory);
            //
            // logger.LogDebug("Extracting {} to {}", explosive.SourcePath, explosive.TargetDirectory);
            // ZipFile.ExtractToDirectory(explosive.SourcePath, explosive.TargetDirectory, true);
            // ProgressStream.OnNext((++downloaded, files.Count + manifest.ExplosiveFiles.Count));

            logger.LogDebug("Extracting {file} to {dir}", explosive.SourcePath, explosive.TargetDirectory);
            using var zip = new ZipArchive(new FileStream(explosive.SourcePath,
                                                          FileMode.Open,
                                                          FileAccess.Read,
                                                          FileShare.Read),
                                           ZipArchiveMode.Read,
                                           false);
            var nested = HasSingleRootDirectory(zip, out var rootDir, !explosive.Unwrap);
            foreach (var entry in zip.Entries)
            {
                if (entry.Length == 0)
                {
                    continue;
                }

                var path = Path.Combine(explosive.TargetDirectory,
                                        nested && !string.IsNullOrEmpty(rootDir)
                                            ? entry.FullName[(rootDir.Length + 1)..]
                                            : entry.FullName);
                // Skip the empty file and directory(Length == 0 as well)
                if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) < entry.LastWriteTime.UtcDateTime)
                {
                    var dir = Path.GetDirectoryName(path);
                    if (dir != null && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    await using var reader = entry.Open();
                    var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Write);
                    await reader.CopyToAsync(writer, cancel.Token).ConfigureAwait(false);
                    await writer.FlushAsync(cancel.Token).ConfigureAwait(false);
                    writer.Close();
                    // 用 await using 会导致处置写入发生在设置属性后而覆盖值
                    File.SetLastWriteTimeUtc(path, entry.LastWriteTime.UtcDateTime);
                }
            }

            ProgressStream.OnNext((++downloaded, files.Count + manifest.ExplosiveFiles.Count));
        }

        Snapshot.Apply(buildDir, entities);

        var importDir = PathDef.Default.DirectoryOfImport(Context.Key);
        var liveDir = PathDef.Default.DirectoryOfLive(Context.Key);
        if (Directory.Exists(liveDir) && Directory.Exists(importDir))
        {
            var queue = new Queue<string>();
            var cleans = new List<string>();
            queue.Enqueue(liveDir);
            while (queue.TryDequeue(out var dir))
            {
                var liveFiles = Directory.GetFiles(dir);
                var liveDirs = Directory.GetDirectories(dir);
                foreach (var sub in liveDirs)
                {
                    queue.Enqueue(sub);
                    cleans.Add(sub);
                }

                foreach (var file in liveFiles)
                {
                    var relative = Path.GetRelativePath(liveDir, file);
                    var target = Path.Combine(importDir, relative);
                    if (!File.Exists(target))
                    {
                        File.Delete(file);
                    }
                }
            }

            // 这里的排序是为了遍历顺序永远是级别深入的在前，以此代替 DFS 达到效果
            // 证明有限遍历到 A 的子文件夹 A/B，由 A/B(3) 长度必定大于 A(1)
            foreach (var target in cleans
                                  .OrderByDescending(x => x.Length)
                                  .Where(Directory.Exists)
                                  .Where(x => Directory.GetDirectories(x).Length == 0
                                           && Directory.GetFiles(x).Length == 0))
            {
                Directory.Delete(target);
            }
        }

        // 生成 allowed_symlinks.txt
        if (!Path.Exists(buildDir))
        {
            Directory.CreateDirectory(buildDir);
        }

        await File
             .WriteAllTextAsync(Path.Combine(buildDir, "allowed_symlinks.txt"),
                                $"""
                                 [prefix]{PathDef.Default.CachePackageDirectory}
                                 [prefix]{PathDef.Default.DirectoryOfLive(Context.Key)}
                                 [prefix]{PathDef.Default.DirectoryOfPersist(Context.Key)}
                                 """,
                                cancel.Token)
             .ConfigureAwait(false);

        watch.Stop();
        logger.LogInformation("Solidifying finished in {ms}ms", watch.ElapsedMilliseconds);

        Context.IsSolidified = true;
    }

    /// <summary>
    ///     检查压缩包是否只有一个根目录，所有文件都在该目录内
    /// </summary>
    /// <param name="archive">要检查的ZipArchive</param>
    /// <param name="rootDirName">如果存在单根目录，返回该目录名</param>
    /// <param name="skip">是否跳过检查，直接返回false</param>
    /// <returns>如果所有文件都在一个根目录内，返回true</returns>
    public static bool HasSingleRootDirectory(
        ZipArchive archive,
        [MaybeNullWhen(false)] out string rootDirName,
        bool skip = false)
    {
        rootDirName = null;

        if (skip || archive.Entries.Count == 0)
        {
            return false;
        }

        // 获取所有条目的路径
        var entries = archive.Entries.Select(e => e.FullName).Where(x => x.Length > 0).ToList();

        foreach (var entry in entries)
        {
            // 获取根级别项目（第一个目录或文件名）
            string rootItem;
            var slashIndex = entry.IndexOf('/');

            if (slashIndex >= 0)
            {
                rootItem = entry[..slashIndex];
            }
            else
                // 如果没有斜杠，则整个条目是根级别项目
            {
                rootItem = entry;
            }

            if (rootDirName is null)
            {
                rootDirName = rootItem;
            }
            else if (rootItem != rootDirName)
            {
                return false;
            }
        }

        rootDirName = string.Empty;
        return true;
    }

    private static bool Verify(string path, string? hash)
    {
        if (File.Exists(path))
        {
            if (hash != null)
            {
                using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var computed = Convert.ToHexString(SHA1.HashData(reader));
                return hash.Equals(computed, StringComparison.InvariantCultureIgnoreCase);
            }

            return true;
        }

        return false;
    }

    public override void Dispose()
    {
        base.Dispose();
        ProgressStream.Dispose();
    }
}
