using TridentCore.Core.Utilities;

namespace TridentCore.Cli.Services;

public static class InstanceIdentityValidator
{
    public static bool TryValidate(string? identity, out string error)
    {
        if (string.IsNullOrWhiteSpace(identity))
        {
            error = "Instance identity is required. Use --identity <key>.";
            return false;
        }

        var sanitized = FileHelper.Sanitize(identity).ToLowerInvariant();
        if (
            string.IsNullOrWhiteSpace(sanitized)
            || sanitized is "." or ".."
            || sanitized.Contains(Path.DirectorySeparatorChar)
            || sanitized.Contains(Path.AltDirectorySeparatorChar)
        )
        {
            error = $"Instance identity '{identity}' does not produce a valid filesystem key.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static string EnsureValid(string identity)
    {
        if (TryValidate(identity, out var error))
        {
            return identity;
        }

        throw new CliException(error, ExitCodes.Usage);
    }
}
