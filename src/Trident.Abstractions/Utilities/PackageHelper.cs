using IParser;
using Trident.Abstractions.Repositories;
using Trident.Abstractions.Repositories.Resources;
using Trident.Purl.Building;
using Trident.Purl.Parsing;

namespace Trident.Abstractions.Utilities;

public static class PackageHelper
{
    public static bool TryParse(string purl, out (string Label, string? Namespace, string Pid, string? Vid) result)
    {
        if (Parser.Default.TryParse(purl, out var parsed))
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

    public static bool IsMatched(string left, string right) =>
        left == right || (TryParse(right, out var r) && IsMatched(left, r.Label, r.Namespace, r.Pid));

    public static bool IsMatched(string left, string label, string? ns, string pid) =>
        TryParse(left, out var l) && l.Label == label && l.Namespace == ns && l.Pid == pid;

    public static string ExtractProjectIdentityIfValid(string purl) =>
        TryParse(purl, out var result) ? ToPurl(result.Label, result.Namespace, result.Pid, null) : purl;

    public static string ToPurl(string label, string? ns, string pid, string? vid) =>
        Builder.Build(label, ns, pid, vid);

    public static string ToPurl(Package package) =>
        ToPurl(package.Label, package.Namespace, package.ProjectId, package.VersionId);

    // vid 存在则固定为特定版本，vid 不存在且 filter 存在则为浮动版本
    public static string Identify(string label, string? ns, string pid, string? vid, Filter? filter) =>
        Builder.Build(label,
                      ns,
                      pid,
                      vid,
                      vid is null && filter is not null
                          ? [("type", filter.Kind?.ToString()), ("version", filter.Version), ("loader", filter.Loader)]
                          : null);
}
