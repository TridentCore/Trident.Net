using System.Diagnostics.CodeAnalysis;
using TridentCore.Pref;
using TridentCore.Pref.Utilities;

namespace TridentCore.Abstractions.Utilities;

public static class ModpackSourceHelper
{
    public const string SCHEME = "modpack";

    private const string PREF_PREFIX = SCHEME + "://pref/";
    private const string LOCAL_PREFIX = SCHEME + "://local/";
    private const string PACKAGE_PREFIX = "pref://";

    public static string FromPref(string pref)
    {
        if (!pref.StartsWith(PACKAGE_PREFIX, StringComparison.OrdinalIgnoreCase)
            || !PackageHelper.TryParseDescriptor(pref, out var descriptor))
        {
            throw new FormatException($"Invalid modpack package reference: {pref}");
        }

        return PREF_PREFIX + descriptor.Build()[PACKAGE_PREFIX.Length..];
    }

    public static string FromLocalName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return LOCAL_PREFIX + Uri.EscapeDataString(name);
    }

    public static bool TryGetPref(string? source, out PackageIdentifier pref)
    {
        pref = default;
        return InternalUriHelper.TryParse(source, SCHEME, out _)
            && source!.StartsWith(PREF_PREFIX, StringComparison.OrdinalIgnoreCase)
            && PackageHelper.TryParse(PACKAGE_PREFIX + source[PREF_PREFIX.Length..], out pref);
    }

    public static bool TryGetLocalName(string? source, [MaybeNullWhen(false)] out string name)
    {
        name = null;
        if (!InternalUriHelper.TryParse(source, SCHEME, out var uri)
            || !source!.StartsWith(LOCAL_PREFIX, StringComparison.OrdinalIgnoreCase)
            || uri.Query.Length != 0)
        {
            return false;
        }

        var encoded = source[LOCAL_PREFIX.Length..];
        if (encoded.Contains('/') || encoded.Contains('\\')
            || !InternalUriHelper.TryDecodeComponent(encoded, out var decoded)
            || string.IsNullOrWhiteSpace(decoded))
        {
            return false;
        }

        name = decoded;
        return true;
    }

    public static bool IsModpack(string? source) => TryGetPref(source, out _) || TryGetLocalName(source, out _);
}
