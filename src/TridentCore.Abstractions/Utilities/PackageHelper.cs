using IParser;
using TridentCore.Abstractions.Repositories;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Pref;
using TridentCore.Pref.Building;
using TridentCore.Pref.Parsing;

namespace TridentCore.Abstractions.Utilities;

public static class PackageHelper
{
    public static bool TryParse(
        string pref,
        out (string Label, string? Namespace, string Pid, string? Vid) result
    )
    {
        if (Parser.Default.TryParse(pref, out var parsed))
        {
            result.Label = parsed.Repository;
            result.Namespace = parsed.Namespace;
            result.Pid = parsed.Identity;
            result.Vid = parsed.Version;

            return true;
        }

        result = default;
        return false;
    }

    public static (string Label, string? Namespace, string Pid, string? Vid) Parse(string pref) =>
        TryParse(pref, out var result)
            ? result
            : throw new System.FormatException($"Invalid package reference: {pref}");

    public static bool IsMatched(string left, string label, string? ns, string pid) =>
        TryParse(left, out var l)
        && string.Equals(l.Label, label, StringComparison.OrdinalIgnoreCase)
        && string.Equals(l.Namespace, ns, StringComparison.Ordinal)
        && string.Equals(l.Pid, pid, StringComparison.Ordinal);

    public static bool IsMatched(string left, string right) =>
        left == right
        || (TryParse(right, out var r) && IsMatched(left, r.Label, r.Namespace, r.Pid));

    public static bool IsMatched(string left, Package right) =>
        IsMatched(left, right.Label, right.Namespace, right.ProjectId);

    public static string ExtractProjectIdentityIfValid(string pref) =>
        TryParse(pref, out var result)
            ? ToPref(result.Label, result.Namespace, result.Pid, null)
            : pref;

    public static string ToPref(string label, string? ns, string pid, string? vid) =>
        Builder.Build(label, ns, pid, vid);

    public static string ToPref(Package package) =>
        ToPref(package.Label, package.Namespace, package.ProjectId, package.VersionId);

    // Normalize a legacy Purl-format string into the new pref:// format when it parses;
    // otherwise return it unchanged so a load never throws on an unrecognized value. Always
    // returns a non-null string (empty input yields an empty string).
    public static string SafeMigrate(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return Parser.Default.TryParse(value, out var parsed) ? parsed.Build() : value;
    }

    // vid 存在则固定为特定版本，vid 不存在且 filter 存在则为浮动版本
    public static string Identify(
        string label,
        string? ns,
        string pid,
        string? vid,
        Filter? filter
    ) =>
        Builder.Build(
            label,
            ns,
            pid,
            vid,
            vid is null && filter is not null
                ?
                [
                    ("type", filter.Kind?.ToString()),
                    ("version", filter.Version),
                    ("loader", filter.Loader),
                ]
                : null
        );
}
