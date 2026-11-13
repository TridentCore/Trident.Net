using System.Text.Json;
using TridentCore.Abstractions.FileModels;

namespace TridentCore.Core.Utilities;

public static class LockStorageHelper
{
    public static async Task<LockData> ReadAsync(string path, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Lock data must be an object.");
        }
        var markers = root.EnumerateObject().Where(x => string.Equals(x.Name, "format", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (markers.Length != 1 || markers[0].Value.ValueKind != JsonValueKind.Number
         || !markers[0].Value.TryGetInt32(out var format) || format != LockData.FORMAT)
        {
            throw new JsonException("Unsupported lock data format; deployment must rebuild the cache.");
        }
        token.ThrowIfCancellationRequested();
        return root.Deserialize<LockData>(JsonSerializerOptions.Web) ?? throw new JsonException("Lock data is empty.");
    }
}
