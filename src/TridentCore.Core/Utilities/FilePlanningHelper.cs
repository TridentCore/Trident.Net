using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Engines.Deploying;

namespace TridentCore.Core.Utilities;

public static class FilePlanningHelper
{
    public static string ProjectionPath(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new InvalidDataException($"Projection path must be relative: {relative}");
        var target = Path.GetFullPath(Path.Combine(root, relative));
        if (FileHelper.IsPathEquivalent(target, root) || !FileHelper.IsInDirectory(target, root))
            throw new InvalidDataException($"Projection path escapes the run directory: {relative}");
        return target;
    }

    public static void RequireFile(DeploymentPlan plan, string path, Uri? url, FileHash? hash, bool executable = false)
    {
        if (!FileHelper.VerifyModified(path, null, hash))
        {
            if (url is null) throw new InvalidDataException($"Local deployment source is missing or changed: {path}");
            var existing = plan.Downloads.FirstOrDefault(x => FileHelper.IsPathEquivalent(x.Path, path));
            if (existing is not null)
            {
                if (existing.Hash != hash || existing.Url != url) throw new InvalidDataException($"Conflicting file requirements: {path}");
                return;
            }
            plan.Downloads.Add(new(path, url, hash, executable));
        }
        else if (executable && !OperatingSystem.IsWindows()
            && (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) == 0)
        {
            plan.Operations.Add(new DeploymentPlan.MakeExecutable(path));
        }
    }

    public static string? LinkTarget(string path) => new FileInfo(path).LinkTarget;

    public static bool LinkMatches(string path, string target)
    {
        var current = LinkTarget(path);
        return current is not null && FileHelper.IsPathEquivalent(Path.GetFullPath(current, Path.GetDirectoryName(path)!), target);
    }
}
