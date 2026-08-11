using IParser;
using TridentCore.Abstractions.Repositories;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Pref;
using TridentCore.Pref.Building;
using TridentCore.Pref.Parsing;

namespace TridentCore.Abstractions.Utilities;

public static class PackageHelper
{
    public static bool TryParse(string pref, out PackageIdentifier result)
    {
        if (Parser.Default.TryParse(pref, out var parsed))
        {
            result = new(parsed.Repository, parsed.Namespace, parsed.Identity, parsed.Version);
            return true;
        }

        result = default;
        return false;
    }

    public static bool TryParseDescriptor(string pref, out PackageDescriptor result)
    {
        if (Parser.Default.TryParse(pref, out var parsed))
        {
            result = parsed;
            return true;
        }

        result = default;
        return false;
    }

    // NOTE: 与 Identify 的 Filter→pref 编码对称：把 descriptor 的 pref 过滤器（type/version/loader）
    //  解码回运行时 Filter，使 floating pref 保持其意图。
    public static PackageIdentifier ToIdentifier(this PackageDescriptor self) =>
        new(self.Repository, self.Namespace, self.Identity, self.Version);

    public static Filter ToFilter(this PackageDescriptor self)
    {
        string? version = null;
        string? loader = null;
        ResourceKind? kind = null;
        foreach (var (key, value) in self.Filters)
        {
            switch (key)
            {
                case "type" when value is not null && Enum.TryParse<ResourceKind>(value, true, out var k):
                    kind = k;
                    break;
                case "version":
                    version = value;
                    break;
                case "loader":
                    loader = value;
                    break;
            }
        }

        return new(version, loader, kind);
    }

    public static PackageIdentifier Parse(string pref) =>
        TryParse(pref, out var result) ? result : throw new FormatException($"Invalid package reference: {pref}");

    public static bool IsMatched(string left, string label, string? ns, string pid) =>
        TryParse(left, out var l)
     && string.Equals(l.Repository, label, StringComparison.OrdinalIgnoreCase)
     && string.Equals(l.Namespace, ns, StringComparison.Ordinal)
     && string.Equals(l.Identity, pid, StringComparison.Ordinal);

    public static bool IsMatched(string left, string right) =>
        left == right || (TryParse(right, out var r) && IsMatched(left, r.Repository, r.Namespace, r.Identity));

    public static bool IsMatched(string left, Package right) =>
        IsMatched(left, right.Label, right.Namespace, right.ProjectId);

    public static string ExtractProjectIdentityIfValid(string pref) =>
        TryParse(pref, out var result) ? ToPref(result.Repository, result.Namespace, result.Identity, null) : pref;

    public static string ToPref(string label, string? ns, string pid, string? vid) =>
        Builder.Build(label, ns, pid, vid);

    public static string ToPref(Package package) =>
        ToPref(package.Label, package.Namespace, package.ProjectId, package.VersionId);

    public static string ToPref(PackageIdentifier id) => ToPref(id.Repository, id.Namespace, id.Identity, id.Version);

    // NOTE: 旧 Purl 字符串能解析时归一化为新 pref:// 格式，否则原样返回——加载绝不因未识别值
    //  抛异常。恒返回非 null 字符串（空输入得空串）。
    public static string SafeMigrate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return Parser.Default.TryParse(value, out var parsed) ? parsed.Build() : value;
    }

    // NOTE: 有 vid 即固定版本；无 vid 但有 filter 即浮动版本。
    public static string Identify(string label, string? ns, string pid, string? vid, Filter? filter) =>
        Builder.Build(label,
                      ns,
                      pid,
                      vid,
                      vid is null && filter is not null
                          ? [("type", filter.Kind?.ToString()), ("version", filter.Version), ("loader", filter.Loader)]
                          : null);
}
