using System.Text.Json;
using System.Text.Json.Serialization;
using MimeDetective;
using MimeDetective.Definitions;
using Trident.Abstractions.Repositories.Resources;
using FileStream = System.IO.FileStream;

namespace Trident.Core.Utilities;

public static class FileHelper
{
    public static readonly JsonSerializerOptions SerializerOptions;

    static FileHelper()
    {
        SerializerOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
        SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        SerializerOptions.Converters.Add(new SystemObjectNewtonsoftCompatibleConverter());
    }

    public static readonly string[] SupportedBitmapMimes =
    [
        "image/jpeg", "image/png", "image/bmp", "image/gif", "image/tiff"
    ];
    public static readonly string[] SupportedBitmapExtensions =
    [
        "jpeg", "jpg", "png", "bmp", "gif", "tiff"
    ];

    private static readonly IContentInspector Inspector =
        new ContentInspectorBuilder { Definitions = DefaultDefinitions.All() }.Build();

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

    public static bool IsPathEquivalent(string path1, string path2) =>
        Path
           .GetFullPath(path1)
           .Equals(Path.GetFullPath(path2),
                   OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public static bool IsFileNameEquivalent(string name1, string name2) =>
        name1.Equals(name2,
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
