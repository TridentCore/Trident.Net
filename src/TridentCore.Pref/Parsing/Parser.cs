using System.Collections.Immutable;
using System.Text.RegularExpressions;
using IParser;

namespace TridentCore.Pref.Parsing;

public partial class Parser : IParser<string, PackageDescriptor>
{
    private static readonly Regex LegacyPattern = GenerateRegex();

    public static Parser Default { get; } = new();

    #region IParser<string,PackageDescriptor> Members

    public PackageDescriptor Parse(string input)
    {
        // New pref:// format: a compliant URL (pref://repository/namespace?/identity@version?filters)
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) && uri.Scheme == "pref")
        {
            return ParsePref(uri);
        }

        // Legacy Purl format: label:namespace/identity@version#filter=value
        return ParseLegacy(input);
    }

    #endregion

    private static PackageDescriptor ParsePref(Uri uri)
    {
        var repository = uri.Host;
        var path = uri.AbsolutePath.TrimStart('/');

        // '@' is a legal pchar, so @version sits at the tail of the path segment
        string? version = null;
        var at = path.LastIndexOf('@');
        var idPath = at >= 0 ? path[..at] : path;
        if (at >= 0)
        {
            version = path[(at + 1)..];
        }

        // optional namespace/identity split
        var slash = idPath.IndexOf('/');
        string? @namespace = null;
        string identity;
        if (slash >= 0)
        {
            @namespace = idPath[..slash];
            identity = idPath[(slash + 1)..];
        }
        else
        {
            identity = idPath;
        }

        return new(repository, @namespace, identity, version, ParseFilters(uri.Query));
    }

    private static ImmutableArray<(string, string?)> ParseFilters(string? query)
    {
        if (string.IsNullOrEmpty(query) || query == "?")
        {
            return [];
        }

        var body = query[0] == '?' ? query[1..] : query;
        var builder = ImmutableArray.CreateBuilder<(string, string?)>();
        foreach (var segment in body.Split('&'))
        {
            var eq = segment.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            builder.Add((segment[..eq], segment[(eq + 1)..]));
        }

        return builder.ToImmutable();
    }

    // COMPAT: legacy Purl string format, kept so old profile/lock/snapshot data still parses.
    // Remove once on-disk Purl data is no longer expected.
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
