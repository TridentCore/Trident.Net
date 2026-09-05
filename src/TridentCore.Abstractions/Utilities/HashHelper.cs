using System.Security.Cryptography;
using System.Text.Json;

namespace TridentCore.Abstractions.Utilities;

public static class HashHelper
{
    // WARNING: ComputeObjectHash 走 System.Text.Json，而 STJ 默认不序列化字段。传 ValueTuple 或
    //  其他只含字段的对象会序列化成 {}——元素个数变化能察觉、内容变化完全察觉不到。
    //  这里强制包含字段，让整个 bug 类在调用侧无法复现，而不是靠每个调用点记住。
    private static readonly JsonSerializerOptions FIELD_FRIENDLY = new() { IncludeFields = true };

    public static string ComputeObjectHash(object obj)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(JsonSerializer.Serialize(obj, FIELD_FRIENDLY));
        stream.Position = 0;
        return FlattenHashBytes(SHA1.HashData(stream));
    }

    public static string FlattenHashBytes(byte[] hash) => Convert.ToHexString(hash);
}
