using System;

namespace TridentCore.Abstractions.Utilities;

/// <summary>
///     内部资源 URI 的通用识别工具：<see cref="IsKind" /> 按 scheme 判定某个 string 是否属于给定的
///     种类（<c>pref</c> / <c>collection</c> 等），统一 <c>&lt;kind&gt;://&lt;identifier&gt;</c> 命名规范。
///     <para>
///         注意：不同种类可能同形不同 scheme（如 <c>pref://repository/...</c> 与
///         <c>collection://by-name/...</c>），因此归属判定必须按 scheme 显式进行，不能用
///         "是否含 <c>://</c>" 一刀切。
///     </para>
/// </summary>
public static class InternalUriHelper
{
    public static bool IsKind(string? s, string kind)
    {
        if (s is null)
        {
            return false;
        }

        var prefix = kind + "://";
        return s.StartsWith(prefix, StringComparison.Ordinal);
    }
}
