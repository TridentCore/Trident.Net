using Trident.Abstractions;
using Trident.Core.Services;

namespace Trident.Cli.Services;

public class InstanceContextResolver(ProfileManager profileManager, LookupContext lookup)
{
    public bool TryResolve(string? instance, string? profilePath, out ResolvedInstanceContext context)
    {
        try
        {
            context = Resolve(instance, profilePath);
            return true;
        }
        catch (CliException)
        {
            if (!string.IsNullOrWhiteSpace(instance) || !string.IsNullOrWhiteSpace(profilePath))
            {
                throw;
            }

            context = null!;
            return false;
        }
    }

    public ResolvedInstanceContext Resolve(string? instance, string? profilePath)
    {
        if (!string.IsNullOrWhiteSpace(instance))
        {
            return ResolveByKey(instance);
        }

        var selectedProfile = profilePath ?? lookup.FoundProfile;
        if (!string.IsNullOrWhiteSpace(selectedProfile))
        {
            return ResolveByProfilePath(selectedProfile);
        }

        throw new CliException(
            "No instance context found. Use --instance <key> or run from an instance directory.",
            ExitCodes.Usage
        );
    }

    private ResolvedInstanceContext ResolveByKey(string key)
    {
        if (!profileManager.TryGetImmutable(key, out var profile))
        {
            throw new CliException($"Instance '{key}' was not found.", ExitCodes.NotFound);
        }

        return new(
            key,
            PathDef.Default.DirectoryOfHome(key),
            PathDef.Default.FileOfProfile(key),
            profile
        );
    }

    private ResolvedInstanceContext ResolveByProfilePath(string profilePath)
    {
        var fullProfilePath = Path.GetFullPath(profilePath);
        var instanceDir = EnsureTrailingSeparator(Path.GetFullPath(PathDef.Default.InstanceDirectory));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!fullProfilePath.StartsWith(instanceDir, comparison))
        {
            throw new CliException(
                $"Profile path '{fullProfilePath}' is outside Trident home.",
                ExitCodes.Usage
            );
        }

        var relative = Path.GetRelativePath(instanceDir, fullProfilePath);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Length != 2 || !string.Equals(parts[1], "profile.json", comparison))
        {
            throw new CliException(
                $"Profile path '{fullProfilePath}' is not a managed instance profile.",
                ExitCodes.Usage
            );
        }

        return ResolveByKey(parts[0]);
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
