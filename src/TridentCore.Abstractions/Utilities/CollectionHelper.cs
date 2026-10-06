using System;
using System.Diagnostics.CodeAnalysis;
using TridentCore.Pref.Utilities;

namespace TridentCore.Abstractions.Utilities;

// collection 是不依赖任何外部资源的就地分组——身份即集合名本身，by-name 段后是 URL 编码的名字，
// 解码即用户可见组名。同名即同组（会合并），改名须重写组内所有 Entry 的 Source。
public static class CollectionHelper
{
    public const string SCHEME = "collection";

    private const string PREFIX = SCHEME + "://";

    private const string BY_NAME_SEGMENT = "by-name/";

    public static string ToUri(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return PREFIX + BY_NAME_SEGMENT + Uri.EscapeDataString(name);
    }

    public static bool TryGetName(string? s, [MaybeNullWhen(false)] out string name)
    {
        name = null;
        if (!InternalUriHelper.TryParse(s, SCHEME, out var uri)
            || !s!.StartsWith(PREFIX + BY_NAME_SEGMENT, StringComparison.OrdinalIgnoreCase)
            || uri.Query.Length != 0)
        {
            return false;
        }

        var encoded = s[(PREFIX.Length + BY_NAME_SEGMENT.Length)..];
        if (encoded.Contains('/') || encoded.Contains('\\')
            || !InternalUriHelper.TryDecodeComponent(encoded, out var decoded)
            || string.IsNullOrWhiteSpace(decoded))
        {
            return false;
        }

        name = decoded;
        return true;
    }
}
