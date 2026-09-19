using System.Text.Json;
using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Engines.Deploying;

namespace TridentCore.Core.Utilities;

public static class DeploymentIndexHelper
{
    public static async Task<AssetIndex?> ReadAssetAsync(LockData.AssetData reference, CancellationToken token = default)
    {
        var path = PathDef.Default.FileOfAssetIndex(reference.Id);
        if (!FileHelper.VerifyModified(path, null, reference.Hash)) return null;
        try
        {
            await using var stream = File.OpenRead(path);
            var index = await JsonSerializer.DeserializeAsync<AssetIndex>(stream, JsonSerializerOptions.Web, token).ConfigureAwait(false);
            if (index?.Objects is null || index.Objects.Values.Any(x => x is null || x.Hash is not { Length: 40 } || !x.Hash.All(char.IsAsciiHexDigit)))
                return null;
            return index;
        }
        catch (JsonException) { return null; }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public static async Task<RuntimeIndex?> ReadRuntimeAsync(uint major, CancellationToken token = default)
    {
        try
        {
            var content = await File.ReadAllTextAsync(PathDef.Default.FileOfRuntimeManifest(major), token).ConfigureAwait(false);
            return ParseRuntime(major, content);
        }
        catch (JsonException) { return null; }
        catch (InvalidDataException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (FormatException) { return null; }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public static RuntimeIndex ParseRuntime(uint major, string content)
    {
        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("files", out var entries) || entries.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Runtime index has no files.");
        var files = new List<RuntimeIndex.Entry>();
        foreach (var entry in entries.EnumerateObject())
        {
            var file = entry.Value;
            if (!file.TryGetProperty("type", out var type)) throw new InvalidDataException("Runtime entry has no type.");
            if (type.GetString() is "directory" or "link") continue;
            if (type.GetString() != "file" || !file.TryGetProperty("downloads", out var downloads)
                || !downloads.TryGetProperty("raw", out var raw)
                || !raw.TryGetProperty("sha1", out var hash) || !raw.TryGetProperty("url", out var url)
                || hash.GetString() is not { Length: 40 } sha1 || !sha1.All(char.IsAsciiHexDigit)
                || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
                throw new InvalidDataException("Invalid runtime file entry.");
            _ = PatchHelper.ResolvePath(PathDef.Default.DirectoryOfRuntime(major), entry.Name);
            files.Add(new(entry.Name, uri, FileHash.Sha1(sha1), file.TryGetProperty("executable", out var executable) && executable.GetBoolean()));
        }
        var java = OperatingSystem.IsWindows() ? "bin/java.exe" : "bin/java";
        if (!files.Any(x => x.Path == java || x.Path.EndsWith("/" + java, StringComparison.Ordinal)))
            throw new InvalidDataException("Runtime index has no Java executable.");
        return new(major, files);
    }
}
