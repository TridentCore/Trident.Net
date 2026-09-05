using System.Security.Cryptography;
using System.Text;

namespace TridentCore.Core.Utilities;

public static class LaunchPlanFileHelper
{
    public const string SOURCE_DIRECTORY_NAME = "source";
    public const string PLAN_SUFFIX = ".plan.json";
    public const string USER_UID_PREFIX = "trident.plan.";

    public static bool IsEnabledPlanFile(string path) =>
        IsPlanFile(path) && !Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal);

    public static bool IsPlanFile(string path) =>
        Path.GetFileName(path).EndsWith(PLAN_SUFFIX, StringComparison.Ordinal);

    public static string SourceUid(string fileName)
    {
        var stem = Path.GetFileName(fileName)[..^PLAN_SUFFIX.Length];
        var index = 0;
        while (index < stem.Length && char.IsAsciiDigit(stem[index]))
        {
            index++;
        }

        if (index > 0 && index < stem.Length && stem[index] == '-')
        {
            index++;
        }

        return SanitizeUid(stem[index..]);
    }

    public static string UserUid(string fileName) =>
        USER_UID_PREFIX + SanitizeUid(Path.GetFileName(fileName)[..^PLAN_SUFFIX.Length]);

    public static string SanitizeUid(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '-');
        }

        var result = builder.ToString().Trim('.', '-', '_');
        return result.Length == 0 ? "layer" : result;
    }

    public static string DisambiguateUid(string uid, string source, ISet<string> used)
    {
        if (used.Add(uid))
        {
            return uid;
        }

        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(source)))[..8].ToLowerInvariant();
        var candidate = $"{uid}-{hash}";
        var suffix = 2;
        while (!used.Add(candidate))
        {
            candidate = $"{uid}-{hash}-{suffix++}";
        }

        return candidate;
    }
}
