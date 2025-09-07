using System.Collections.ObjectModel;

namespace Trident.Core.Engines.Deploying;

public class Snapshot : Collection<Snapshot.Entity>
{
    public static Snapshot Take(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Directory {directory} not found");
        }

        var snapshot = new Snapshot();
        var subs = new Queue<DirectoryInfo>();
        subs.Enqueue(new(directory));
        while (subs.TryDequeue(out var parent))
        {
            var dirs = parent.GetDirectories();
            foreach (var dir in dirs)
            {
                if (dir.LinkTarget != null)
                {
                    snapshot.Add(new(dir.FullName, dir.LinkTarget, true));
                }

                subs.Enqueue(dir);
            }

            var files = parent.GetFiles();
            foreach (var file in files)
            {
                if (file.LinkTarget != null)
                {
                    snapshot.Add(new(file.FullName, file.LinkTarget, false));
                }
            }
        }

        return snapshot;
    }

    public static void Apply(string directory, IReadOnlyList<Entity> toPopulate)
    {
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var current = Take(directory);
        var entities = new List<Entity>(toPopulate.DistinctBy(x => x.Path));
        foreach (var exist in current)
        {
            var final = entities.FirstOrDefault(x => x.Path == exist.Path);
            if (final != null)
            {
                if (!exist.Target.Equals(final.Target))
                {
                    if (exist.IsDirectory)
                    {
                        Directory.Delete(exist.Path, false);
                        Directory.CreateSymbolicLink(exist.Path, final.Target);
                    }
                    else
                    {
                        File.Delete(exist.Path);
                        File.CreateSymbolicLink(exist.Path, final.Target);
                    }
                }

                entities.Remove(final);
            }
            else
            {
                // 不该出现的多余的软链接
                if (exist.IsDirectory)
                {
                    Directory.Delete(exist.Path, false);
                }
                else
                {
                    File.Delete(exist.Path);
                }
            }
        }

        foreach (var remain in entities)
        {
            var dir = Path.GetDirectoryName(remain.Path);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // 被普通文件或目录占用了
            if (File.Exists(remain.Path) && File.ResolveLinkTarget(remain.Path, false) is null
             || Directory.Exists(remain.Path) && Directory.ResolveLinkTarget(remain.Path, false) is null)
            {
                throw new InvalidOperationException($"Target {remain.Path} already exists and is not a symlink");
            }

            if (remain.IsDirectory)
            {
                Directory.CreateSymbolicLink(remain.Path, remain.Target);
            }
            else
            {
                File.CreateSymbolicLink(remain.Path, remain.Target);
            }
        }
    }

    #region Nested type: Entity

    public record Entity(string Path, string Target, bool IsDirectory);

    #endregion
}
