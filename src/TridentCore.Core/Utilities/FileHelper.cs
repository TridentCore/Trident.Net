using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MimeDetective;
using MimeDetective.Definitions;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Core.Converters;
using FileHash = TridentCore.Abstractions.Utilities.FileHash;
using HashAlgorithm = TridentCore.Abstractions.Utilities.HashAlgorithm;
using FileStream = System.IO.FileStream;

namespace TridentCore.Core.Utilities;

public static class FileHelper
{
    public static readonly JsonSerializerOptions SerializerOptions;

    public static readonly string[] SupportedBitmapMimes =
    [
        "image/jpeg", "image/png", "image/bmp", "image/gif", "image/tiff"
    ];

    public static readonly string[] SupportedBitmapExtensions = ["jpeg", "jpg", "png", "bmp", "gif", "tiff"];

    private static readonly IContentInspector INSPECTOR = new ContentInspectorBuilder
    {
        Definitions = DefaultDefinitions.All()
    }.Build();

    static FileHelper()
    {
        SerializerOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
        SerializerOptions.Converters.Add(new SelectorTypeConverter());
        SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        SerializerOptions.Converters.Add(new SystemObjectNewtonsoftCompatibleConverter());
    }

    // Linux defaults to case-sensitive filesystems; Windows and macOS default to
    // case-insensitive-but-case-preserving, so path and name comparisons follow suit.
    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

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
                // On case-insensitive but case-preserving volumes the candidate string may not
                // match the on-disk casing, so resolve the real path to keep downstream string
                // comparisons consistent.
                var dir = Path.GetDirectoryName(path);
                var name = Path.GetFileName(path);
                if (dir is not null && name is not null)
                {
                    var real = Directory.GetFiles(dir, name).FirstOrDefault();
                    if (real is not null)
                    {
                        return real;
                    }
                }

                return path;
            }
        }

        return null;
    }

    public static bool IsBitmapFile(string path)
    {
        if (File.Exists(path))
        {
            var results = INSPECTOR.Inspect(path).ByMimeType();
            if (results.Any(x => SupportedBitmapMimes.Contains(x.MimeType)))
            {
                return true;
            }
        }

        return false;
    }

    public static string GuessBitmapExtension(Stream stream, string fallback = "png") =>
        INSPECTOR.Inspect(stream).ByFileExtension().OrderBy(x => -x.Points).Select(x => x.Extension).FirstOrDefault()
     ?? fallback;

    // NOTE: Normalization below is lexical only. Path.GetFullPath neither resolves
    //  symbolic links nor knows about per-volume case sensitivity, and relative inputs
    //  resolve against the process working directory, so callers must supply absolute,
    //  already-resolved paths whenever those distinctions matter. Applies to the
    //  IsInDirectory / IsPathEquivalent pair below.
    public static bool IsInDirectory(string file, string directory)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(file).StartsWith(prefix, PathComparison);
    }

    public static bool IsPathEquivalent(string? path1, string? path2)
    {
        if (string.IsNullOrEmpty(path1) || string.IsNullOrEmpty(path2))
        {
            return false;
        }

        var normalizedLeft = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path1));
        var normalizedRight = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path2));
        return normalizedLeft.Equals(normalizedRight, PathComparison);
    }

    public static bool IsFileNameEquivalent(string? name1, string? name2) =>
        !string.IsNullOrEmpty(name1) && !string.IsNullOrEmpty(name2) && name1.Equals(name2, PathComparison);

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
            _ => throw new NotImplementedException()
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

    public static bool VerifyModified(string path, DateTimeOffset? modifiedTime, FileHash? hash)
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
                var computed = ComputeHash(reader, hash.Algorithm);
                if (hash.Value.Equals(computed, StringComparison.InvariantCultureIgnoreCase))
                {
                    // 文件没变，写回修改时间避免下次重复检查
                    if (modifiedTime.HasValue)
                    {
                        File.SetLastWriteTimeUtc(path, modifiedTime.Value.UtcDateTime);
                    }

                    // 文件相同直接通过
                    return true;
                }

                // 提供了 hash 但是没通过，算判定失败
                return false;
            }

            // 修改了，但是没有提供 hash，判定为存在性检验，直接通过
            return true;
        }

        return false;
    }

    public static string ComputeHash(string path, HashAlgorithm algorithm)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ComputeHash(stream, algorithm);
    }

    public static async ValueTask<string> ComputeHashAsync(string path, HashAlgorithm algorithm)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await ComputeHashAsync(stream, algorithm).ConfigureAwait(false);
    }

    public static string ComputeHash(Stream stream, HashAlgorithm algorithm) =>
        Convert.ToHexString(SelectHashAlgorithm(algorithm).ComputeHash(stream));

    public static async ValueTask<string> ComputeHashAsync(Stream stream, HashAlgorithm algorithm) =>
        Convert.ToHexString(await SelectHashAlgorithm(algorithm).ComputeHashAsync(stream).ConfigureAwait(false));

    private static System.Security.Cryptography.HashAlgorithm SelectHashAlgorithm(HashAlgorithm algorithm) =>
        algorithm switch
        {
            HashAlgorithm.Sha1 => SHA1.Create(),
            HashAlgorithm.Sha256 => SHA256.Create(),
            HashAlgorithm.Sha512 => SHA512.Create(),
            HashAlgorithm.Md5 => MD5.Create(),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null)
        };

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
