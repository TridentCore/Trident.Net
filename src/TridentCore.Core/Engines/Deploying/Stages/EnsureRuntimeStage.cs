using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TridentCore.Abstractions;
using TridentCore.Core.Exceptions;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;
using FileHash = TridentCore.Abstractions.Utilities.FileHash;

namespace TridentCore.Core.Engines.Deploying.Stages;

public class EnsureRuntimeStage(
    MojangService mojangService,
    IHttpClientFactory httpClientFactory,
    ILogger<EnsureRuntimeStage> logger) : StageBase
{
    protected override async Task OnProcessAsync(CancellationToken token)
    {
        var majors = Context.Resolution.JavaMajors;
        try
        {
            Context.Java = await Context.JavaHomeLocator(majors, token).ConfigureAwait(false);
            if (Context.Java.Origin == JavaHelper.JavaResolution.Source.UserConfigured)
            {
                return;
            }
        }
        catch (JavaNotFoundException)
        {
            Context.Java = null!;
        }

        var candidates = Context.Java is null ? majors : [Context.Java.Major];
        foreach (var major in candidates)
        {
            Context.Java ??= new(JavaHelper.BundledHome(major), JavaHelper.JavaResolution.Source.Bundled,
                                 major, PlatformHelper.GetOsArch());
            var runtime = Context.BaseLock?.Runtime;
            Context.Lock = Context.Lock with
            {
                Runtime = runtime is not null && runtime.Major == major
                       && runtime.Os == PlatformHelper.GetOsName() && runtime.Architecture == Context.Java.Architecture
                    ? runtime : null
            };
            var content = await LoadManifestAsync(major, token).ConfigureAwait(false);
            if (content is null)
            {
                Context.Java = null!;
                continue;
            }
            var (files, links) = ParseRuntimeFiles(content);
            if (files.Count == 0) throw new FormatException($"Java {major} runtime manifest contains no files");
            Context.Runtime = new(major, files, links);
            return;
        }
        throw new JavaNotFoundException(majors[0]);
    }

    // 解析按主版本的运行时 manifest JSON。runtimes/{major}.json 的 sha1 与锁内指纹匹配时
    // 返回缓存（离线快路径）；否则拉 Mojang 运行时索引、下载 manifest、按索引 sha1 校验、
    // 持久化并记录指纹供下次使用。
    private async Task<string?> LoadManifestAsync(uint major, CancellationToken token)
    {
        var path = PathDef.Default.FileOfRuntimeManifest(major);
        var recorded = Context.Lock.Runtime;

        if (recorded is { } fingerprint
         && File.Exists(path)
         && FileHelper.VerifyModified(path, null, FileHash.Sha1(fingerprint.Sha1)))
        {
            return await File.ReadAllTextAsync(path, token).ConfigureAwait(false);
        }

        var manifest = await mojangService.GetRuntimeManifestAsync().ConfigureAwait(false);
        var osString = GenerateOsString(Context.Java.Architecture);
        var first = manifest.TryGetValue(osString, out var runtimes)
            ? runtimes.Values.SelectMany(x => x).FirstOrDefault(x => JavaHelper.ParseJavaMajor(x.Version.Name) == major)
            : null;
        if (first is null)
        {
            logger.LogWarning("No Java {major} runtime is available for {os}", major, osString);
            return null;
        }
        using var client = httpClientFactory.CreateClient(RepositoryAgent.CLIENT_NAME);

        var dir = Path.GetDirectoryName(path);
        if (dir is { } parent && !Directory.Exists(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var tmp = path + ".downloading";
        await using (var stream = await client.GetStreamAsync(first.Manifest.Url, token).ConfigureAwait(false))
        await using (var writer = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.Write))
        {
            await stream.CopyToAsync(writer, token).ConfigureAwait(false);
            await writer.FlushAsync(token).ConfigureAwait(false);
        }

        if (!FileHelper.VerifyModified(tmp, null, FileHash.Sha1(first.Manifest.Sha1)))
        {
            File.Delete(tmp);
            throw new FormatException($"Downloaded runtime manifest for Java {major} failed sha1 verification");
        }

        File.Move(tmp, path, true);

        Context.Lock = Context.Lock with { Runtime = new(major, first.Manifest.Sha1, PlatformHelper.GetOsName(), Context.Java.Architecture) };

        return await File.ReadAllTextAsync(path, token).ConfigureAwait(false);
    }

    private static (List<BundledRuntime.File> Files, List<BundledRuntime.Link> Links) ParseRuntimeFiles(string content)
    {
        var files = new List<BundledRuntime.File>();
        var links = new List<BundledRuntime.Link>();
        var json = JsonSerializer.Deserialize<JsonObject>(content, JsonSerializerOptions.Default);
        if (json is not null && json.TryGetPropertyValue("files", out var v) && v is JsonObject entries)
        {
            foreach (var (path, value) in entries)
            {
                if (value is not JsonObject file)
                {
                    throw new FormatException("Invalid file entry");
                }

                var type = file["type"]?.GetValue<string>() ?? throw new FormatException("Invalid type property");

                switch (type)
                {
                    case "directory":
                        continue;
                    case "file":
                        {
                            if (file.TryGetPropertyValue("downloads", out var d)
                             && d is JsonObject downloads
                             && downloads.TryGetPropertyValue("raw", out var r)
                             && r is JsonObject raw)
                            {
                                var executable = file["executable"]?.GetValue<bool>() ?? false;
                                var sha1 = raw["sha1"]?.GetValue<string>()
                                        ?? throw new FormatException("Invalid sha1 property");
                                var urlString = raw["url"]?.GetValue<string>()
                                             ?? throw new FormatException("Invalid url property");
                                var url = Uri.IsWellFormedUriString(urlString, UriKind.Absolute)
                                              ? new Uri(urlString)
                                              : throw new FormatException("Invalid url string");
                                files.Add(new(path, url, FileHash.Sha1(sha1), executable));
                            }
                            else
                            {
                                throw new FormatException("Invalid downloads property");
                            }

                            break;
                        }
                    case "link":
                        {
                            // 目标解析到运行时目录之外且互相冲突，忽略链接。
                            break;
                        }
                }
            }
        }

        return (files, links);
    }

    private static string GenerateOsString(string architecture)
    {
        if (OperatingSystem.IsWindows())
        {
            if (architecture == "x64")
            {
                return "windows-x64";
            }

            if (architecture == "x86")
            {
                return "windows-x86";
            }

            if (architecture == "arm64")
            {
                return "windows-arm64";
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            if (architecture == "x64")
            {
                return "linux";
            }

            if (architecture == "x86")
            {
                return "linux-i386";
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            if (architecture == "x64")
            {
                return "mac-os";
            }

            if (architecture == "arm64")
            {
                return "mac-os-arm64";
            }
        }

        throw new NotSupportedException("Unsupported operating system.");
    }
}
