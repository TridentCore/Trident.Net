using System.Security.Cryptography;
using System.Text.Json;

namespace Trident.Abstractions.Utilities;

public static class HashHelper
{
    public static string ComputeObjectHash(object obj)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(JsonSerializer.Serialize(obj));
        stream.Position = 0;
        return Convert.ToHexString(SHA1.HashData(stream));
    }
}
