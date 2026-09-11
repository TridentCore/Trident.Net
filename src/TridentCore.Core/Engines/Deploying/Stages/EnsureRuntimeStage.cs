using System.Runtime.InteropServices;
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
        var major = Context.Lock.Artifact!.JavaMajorVersion;
        if (Context.Lock.Runtime?.Major != major)
        {
            Context.Lock = Context.Lock with { Runtime = null };
        }

        // NOTE: 仅捆绑运行时（或尚未安装者）由管线管理、可 manifest 自愈；用户给定的 JRE 原样使用，只校验可用性。
        bool needManifest;
        JavaHelper.JavaResolution? resolution = null;
        try
        {
            resolution = Context.JavaHomeLocator(major);
            needManifest = resolution.Origin == JavaHelper.JavaResolution.Source.Bundled;
        }
        catch (JavaNotFoundException)
        {
            needManifest = true;
        }

        if (!needManifest)
        {
            JavaHelper.EnsureUsable(resolution!, major);
            return;
        }

        var content = await LoadManifestAsync(major, token).ConfigureAwait(false);
        var (files, links) = ParseRuntimeFiles(content);
        var executable = OperatingSystem.IsWindows() ? "bin/java.exe" : "bin/java";
        if (!files.Any(x => x.Path == executable || x.Path.EndsWith("/" + executable, StringComparison.Ordinal)))
        {
            throw new JavaNotFoundException(major);
        }

        Context.Runtime = new(major, files, links);
    }

    // 解析按主版本的运行时 manifest JSON。runtimes/{major}.json 的 sha1 与锁内指纹匹配时
    // 返回缓存（离线快路径）；否则拉 Mojang 运行时索引、下载 manifest、按索引 sha1 校验、
    // 持久化并记录指纹供下次使用。
    private async Task<string> LoadManifestAsync(uint major, CancellationToken token)
    {
        var path = PathDef.Default.FileOfRuntimeManifest(major);
        var recorded = Context.Lock.Runtime;

        if (recorded is { } fingerprint
         && JavaHelper.ParseJavaMajor(fingerprint.Version) == major
         && File.Exists(path)
         && FileHelper.VerifyModified(path, null, FileHash.Sha1(fingerprint.Sha1)))
        {
            return await File.ReadAllTextAsync(path, token).ConfigureAwait(false);
        }

        var manifest = await mojangService.GetRuntimeManifestAsync().ConfigureAwait(false);
        var osString = GenerateOsString();
        var runtimeString = GenerateRuntimeString(major);
        if (osString is null || runtimeString is null
         || !manifest.TryGetValue(osString, out var runtimes)
         || !runtimes.TryGetValue(runtimeString, out var candidates))
        {
            throw new JavaNotFoundException(major);
        }
        var first = candidates
                    .Where(x => JavaHelper.ParseJavaMajor(x.Version.Name) == major)
                    .OrderByDescending(x => x.Version.Released)
                    .FirstOrDefault()
                 ?? throw new JavaNotFoundException(major);
        logger.LogInformation("Selected Java {version} for requested major {major}", first.Version.Name, major);
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

        Context.Lock = Context.Lock with { Runtime = new(major, first.Manifest.Sha1, first.Version.Name) };

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

    // NOTE: 显式 family 映射而非全表扫描——Mojang 只有在新增 major 时才会新增 family，那属于
    //  需要 Trident 跟进的源变化；全表扫描会把 minecraft-java-exe 这类非运行时条目和
    //  java-runtime-*-snapshot 一起纳入候选。family 内的补丁版本仍取 released 最新，无需改代码。
    private static string? GenerateRuntimeString(uint major) =>
        major switch
        {
            8 => "jre-legacy",
            16 => "java-runtime-alpha",
            17 => "java-runtime-gamma",
            21 => "java-runtime-delta",
            25 => "java-runtime-epsilon",
            _ => null
        };

    private static string? GenerateOsString()
    {
        if (OperatingSystem.IsWindows())
        {
            if (RuntimeInformation.OSArchitecture == Architecture.X64)
            {
                return "windows-x64";
            }

            if (RuntimeInformation.OSArchitecture == Architecture.X86)
            {
                return "windows-x86";
            }

            if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
            {
                return "windows-arm64";
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            if (RuntimeInformation.OSArchitecture == Architecture.X64)
            {
                return "linux";
            }

            if (RuntimeInformation.OSArchitecture == Architecture.X86)
            {
                return "linux-i386";
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            if (RuntimeInformation.OSArchitecture == Architecture.X64)
            {
                return "mac-os";
            }

            if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
            {
                return "mac-os-arm64";
            }
        }

        return null;
    }
}
