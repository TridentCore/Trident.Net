using System;
using System.Diagnostics.CodeAnalysis;

namespace TridentCore.Pref.Utilities;

public static class InternalUriHelper
{
    public static bool HasScheme(string? source, string scheme) =>
        source is not null && source.StartsWith(scheme + ":", StringComparison.OrdinalIgnoreCase);

    public static bool TryParse(string? source, string scheme, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        if (source is null || !source.StartsWith(scheme + "://", StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(source, UriKind.Absolute, out var parsed)
            || !parsed.Scheme.Equals(scheme, StringComparison.OrdinalIgnoreCase)
            || parsed.UserInfo.Length != 0 || parsed.Port != -1 || parsed.Fragment.Length != 0)
        {
            return false;
        }

        var authority = source.AsSpan(scheme.Length + 3);
        var end = authority.IndexOfAny('/', '?', '#');
        if (end >= 0)
        {
            authority = authority[..end];
        }

        if (authority.IsEmpty || !authority.Equals(parsed.Host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    public static bool TryDecodeComponent(string encoded, [NotNullWhen(true)] out string? value)
    {
        value = null;
        for (var i = 0; i < encoded.Length; i++)
        {
            if (encoded[i] != '%')
            {
                continue;
            }

            if (i + 2 >= encoded.Length || !Uri.IsHexDigit(encoded[i + 1]) || !Uri.IsHexDigit(encoded[i + 2]))
            {
                return false;
            }

            i += 2;
        }

        value = Uri.UnescapeDataString(encoded);
        return true;
    }
}
