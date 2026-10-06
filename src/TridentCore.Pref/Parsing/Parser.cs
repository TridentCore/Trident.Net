using System.Collections.Immutable;
using System.Text.RegularExpressions;
using IParser;
using TridentCore.Pref.Utilities;

namespace TridentCore.Pref.Parsing;

public partial class Parser : IParser<string, PackageDescriptor>
{
    private static readonly Regex LegacyPattern = GenerateRegex();

    public static Parser Default { get; } = new();

    #region IParser<string,PackageDescriptor> Members

    public PackageDescriptor Parse(string input)
    {
        if (InternalUriHelper.HasScheme(input, Building.Builder.Scheme))
        {
            if (!InternalUriHelper.TryParse(input, Building.Builder.Scheme, out var uri)
                || string.IsNullOrEmpty(uri.Host))
            {
                throw new FormatException("Invalid package reference URI.");
            }

            return ParsePref(input, uri);
        }

        return ParseLegacy(input);
    }

    #endregion

    private static PackageDescriptor ParsePref(string input, Uri uri)
    {
        var repository = uri.Host;
        var pathStart = input.IndexOf('/', Building.Builder.Scheme.Length + 3);
        var queryStart = input.IndexOf('?');
        if (pathStart < 0 || queryStart >= 0 && pathStart > queryStart)
        {
            throw new FormatException("A package reference requires an identity.");
        }

        // NOTE: Read escaped components from the input so URI dot-segment normalization cannot change package IDs.
        var path = queryStart < 0 ? input[(pathStart + 1)..] : input[(pathStart + 1)..queryStart];
        if (path.Contains('\\'))
        {
            throw new FormatException("Package identity delimiters must be escaped.");
        }

        // NOTE: '@' 是合法 pchar，@version 落在路径段尾部。
        string? version = null;
        var at = path.LastIndexOf('@');
        var idPath = at >= 0 ? path[..at] : path;
        if (at >= 0)
        {
            // WARNING: Persisted Packwiz versions may contain unescaped '/' from Git branch names.
            version = DecodeRequired(path[(at + 1)..]);
        }

        if (idPath.Contains('@'))
        {
            throw new FormatException("Package identity delimiters must be escaped.");
        }

        var slash = idPath.IndexOf('/');
        string? @namespace = null;
        string identity;
        if (slash >= 0)
        {
            @namespace = DecodeRequired(idPath[..slash]);
            identity = DecodeRequired(idPath[(slash + 1)..]);
            if (idPath[(slash + 1)..].Contains('/'))
            {
                throw new FormatException("Package identity delimiters must be escaped.");
            }
        }
        else
        {
            identity = DecodeRequired(idPath);
        }

        return new(repository, @namespace, identity, version,
                   ParseFilters(queryStart < 0 ? null : input[(queryStart + 1)..]));
    }

    private static string DecodeRequired(string encoded) =>
        InternalUriHelper.TryDecodeComponent(encoded, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new FormatException("A package reference contains an invalid or empty component.");

    private static ImmutableArray<(string, string?)> ParseFilters(string? query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<(string, string?)>();
        foreach (var segment in query.Split('&'))
        {
            var eq = segment.IndexOf('=');
            if (eq <= 0)
            {
                throw new FormatException("A package filter requires a key and a value separated by '='.");
            }

            if (!InternalUriHelper.TryDecodeComponent(segment[(eq + 1)..], out var value))
            {
                throw new FormatException("A package filter contains invalid escaping.");
            }

            builder.Add((DecodeRequired(segment[..eq]), value));
        }

        return builder.ToImmutable();
    }

    // TODO: 旧 Purl 字符串格式兼容，保留以解析存量 profile/lock/snapshot 数据；
    //  磁盘 Purl 数据不再需要后移除。
    private PackageDescriptor ParseLegacy(string input)
    {
        var match = LegacyPattern.Match(input);
        if (match.Success
         && match.Groups.TryGetValue("label", out var label)
         && match.Groups.TryGetValue("identity", out var identity))
        {
            match.Groups.TryGetValue("namespace", out var @namespace);
            match.Groups.TryGetValue("version", out var version);
            match.Groups.TryGetValue("filter", out var filters);
            match.Groups.TryGetValue("value", out var values);

            if (filters is { Captures.Count: > 0 }
             && values is { Captures.Count: > 0 }
             && filters.Captures.Count == values.Captures.Count)
            {
                return new(label.Value,
                           @namespace is { Success: true } ? @namespace.Value : null,
                           identity.Value,
                           version is { Success: true } ? version.Value : null,
                           [.. filters.Captures.Zip(values.Captures, (x, y) => (x.Value, y.Value))]);
            }

            return new(label.Value,
                       @namespace is { Success: true } ? @namespace.Value : null,
                       identity.Value,
                       version is { Success: true } ? version.Value : null,
                       []);
        }

        throw new FormatException();
    }

    [GeneratedRegex("^(?<label>[a-zA-Z0-9._-]+):((?<namespace>[a-zA-Z0-9._-]+)/)?(?<identity>[a-zA-Z0-9._-]+)(@(?<version>[a-zA-Z0-9._-]+))?(#(?<filter>[a-zA-Z0-9._-]+)=(?<value>[a-zA-Z0-9._-]+))*$",
                    RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex GenerateRegex();
}
