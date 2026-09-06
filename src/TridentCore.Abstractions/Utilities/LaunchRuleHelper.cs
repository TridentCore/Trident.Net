using System.Text.RegularExpressions;
using TridentCore.Abstractions.Launching;

namespace TridentCore.Abstractions.Utilities;

public static class LaunchRuleHelper
{
    public static string NormalizeArchitecture(string architecture) => architecture.ToLowerInvariant() switch
    {
        "amd64" or "x86_64" or "x64" => "x64",
        "aarch64" or "arm64" => "arm64",
        "i386" or "i486" or "i586" or "i686" or "x86" => "x86",
        "arm" or "arm32" or "armv7l" => "arm",
        _ => throw new NotSupportedException($"Unsupported Java architecture '{architecture}'")
    };

    public static bool Allows(IReadOnlyList<LaunchRule>? rules, LaunchTarget target)
    {
        if (rules is null || rules.Count == 0)
        {
            return true;
        }

        var allowed = false;
        foreach (var rule in rules)
        {
            if (Matches(rule, target))
            {
                allowed = rule.Allow;
            }
        }
        return allowed;
    }

    private static bool Matches(LaunchRule rule, LaunchTarget target) =>
        (rule.Os is null || rule.Os == target.Os || rule.Os == $"{target.Os}-{target.Architecture}")
     && (rule.Architecture is null || Regex.IsMatch(target.Architecture, rule.Architecture,
                                                    RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
     && (rule.OsVersion is null || Regex.IsMatch(target.OsVersion, rule.OsVersion,
                                                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));
}
