using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MimeDetective;
using MimeDetective.Definitions;
using TridentCore.Abstractions.Repositories.Resources;
using FileStream = System.IO.FileStream;

namespace TridentCore.Core.Utilities;

public static class FileHelper
{
    public static readonly JsonSerializerOptions SerializerOptions;

    public static readonly string[] SupportedBitmapMimes =
    [
        "image/jpeg", "image/png", "image/bmp", "image/gif", "image/tiff",
    ];

    public static readonly string[] SupportedBitmapExtensions = ["jpeg", "jpg", "png", "bmp", "gif", "tiff",];

    private static readonly IContentInspector Inspector = new ContentInspectorBuilder
    {
        Definitions = DefaultDefinitions.All(),
    }.Build();

    static FileHelper()
    {
        SerializerOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
        SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        SerializerOptions.Converters.Add(new SystemObjectNewtonsoftCompatibleConverter());
    }

    public static string Sanitize(string fileName)
    {
        var sanitized = !string.IsNullOrEmpty(fileName)
                            ? string.Join(string.Empty,
                                          fileName
                                             .Trim()
                                             .Where(x => !Path.GetInvalidFileNameChars().Contains(x))
                                             .Select(x => x is ' ' or '-' ? '_' : x))
                            : "_";
        while (sanitized.Contains("__"))
        {
            sanitized = sanitized.Replace("__", "_");
        }

        return sanitized;
    }

    public static string? PickExists(string home, Span<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            var path = Path.Combine(home, candidate);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    public static bool IsBitmapFile(string path)
    {
        if (File.Exists(path))
        {
            var results = Inspector.Inspect(path).ByMimeType();
            if (results.Any(x => SupportedBitmapMimes.Contains(x.MimeType)))
            {
                return true;
            }
        }

        return false;
    }

    public static string GuessBitmapExtension(Stream stream, string fallback = "png") =>
        Inspector.Inspect(stream).ByFileExtension().OrderBy(x => -x.Points).Select(x => x.Extension).FirstOrDefault()
     ?? fallback;

    public static bool IsInDirectory(string file, string directory) =>
        Path
           .GetFullPath(file)
           .StartsWith(Path.GetFullPath(directory),
                       OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public static bool IsPathEquivalent(string? path1, string? path2)
    {
        if (string.IsNullOrEmpty(path1) || string.IsNullOrEmpty(path2))
        {
            return false;
        }

        var normalizedLeft = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path1));
        var normalizedRight = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path2));
        return normalizedLeft.Equals(normalizedRight,
                                     OperatingSystem.IsWindows()
                                         ? StringComparison.OrdinalIgnoreCase
                                         : StringComparison.Ordinal);
    }

    public static bool IsFileNameEquivalent(string? name1, string? name2) =>
        !string.IsNullOrEmpty(name1)
     && !string.IsNullOrEmpty(name2)
     && name1.Equals(name2,
                     OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public static async Task TryWriteToFileAsync(string path, Stream stream)
    {
        var parent = Path.GetDirectoryName(path);
        if (parent != null && !Directory.Exists(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var writer = new FileStream(path, FileMode.Create, FileAccess.Write);
        await stream.CopyToAsync(writer).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
        writer.Close();
    }

    public static async Task TryWriteToFileAsync(string path, ReadOnlyMemory<byte> content)
    {
        var parent = Path.GetDirectoryName(path);
        if (parent != null && !Directory.Exists(parent))
        {
            Directory.CreateDirectory(parent);
        }

        await using var writer = new FileStream(path, FileMode.Create, FileAccess.Write);
        await writer.WriteAsync(content).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
    }

    public static string GetAssetFolderName(ResourceKind kind) =>
        kind switch
        {
            ResourceKind.Mod => "mods",
            ResourceKind.World => "saves",
            ResourceKind.ShaderPack => "shaderpacks",
            ResourceKind.ResourcePack => "resourcepacks",
            ResourceKind.DataPack => "datapacks",
            _ => throw new NotImplementedException(),
        };

    public static (ulong, ulong) CalculateDirectorySize(string path)
    {
        if (!Directory.Exists(path))
        {
            return (0ul, 0ul);
        }

        var directory = new DirectoryInfo(path);
        var (size, count) = directory
                           .GetFiles()
                           .Aggregate((0ul, 0ul),
                                      (current, file) => (current.Item1 + (ulong)file.Length, current.Item2 + 1));
        return directory
              .GetDirectories()
              .Aggregate((size, count),
                         (current, dir) =>
                         {
                             var (subSize, subCount) = CalculateDirectorySize(dir.FullName);
                             return (current.size + subSize, current.count + subCount);
                         });
    }

    public static bool VerifyModified(string path, DateTimeOffset? modifiedTime, string? hash)
    {
        if (File.Exists(path))
        {
            if (modifiedTime != null)
            {
                var mtime = File.GetLastWriteTimeUtc(path);
                if (mtime == modifiedTime)
                {
                    // 没被修改，直接通过
                    return true;
                }
            }

            if (hash != null)
            {
                using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var computed = Convert.ToHexString(SHA1.HashData(reader));
                if (hash.Equals(computed, StringComparison.InvariantCultureIgnoreCase))
                {
                    // 文件没变，写回修改时间避免下次重复检查
                    if (modifiedTime.HasValue)
                    {
                        File.SetLastWriteTimeUtc(path, modifiedTime.Value.UtcDateTime);
                    }

                    // 文件相同直接通过
                    return true;
                }
                else
                {
                    // 提供了 hash 但是没通过，算判定失败
                    return false;
                }
            }

            // 修改了，但是没有提供 hash，判定为存在性检验，直接通过
            return true;
        }

        return false;
    }

    #region Nested type: SystemObjectNewtonsoftCompatibleConverter

    private class SystemObjectNewtonsoftCompatibleConverter : JsonConverter<object>
    {
        public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.True:
                    return true;
                case JsonTokenType.False:
                    return false;
                case JsonTokenType.Number when reader.TryGetInt64(out var l):
                    return l;
                case JsonTokenType.Number:
                    return reader.GetDouble();
                case JsonTokenType.String when reader.TryGetDateTime(out var datetime):
                    return datetime;
                case JsonTokenType.String:
                    return reader.GetString();
                default:
                {
                    // Use JsonElement as fallback.
                    // Newtonsoft uses JArray or JObject.
                    using var document = JsonDocument.ParseValue(ref reader);
                    return document.RootElement.Clone();
                }
            }
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options) =>
            writer.WriteRawValue(JsonSerializer.Serialize(value));
    }

    #endregion
}
