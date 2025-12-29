using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trident.Core.Exceptions;
using Trident.Core.Services;

namespace Trident.Core.Engines.Deploying.Stages;

public class EnsureRuntimeStage(MojangService mojangService, IHttpClientFactory httpClientFactory) : StageBase
{
    protected override async Task OnProcessAsync(CancellationToken token)
    {
        var major = Context.Artifact!.JavaMajorVersion;

        var bad = false;

        try
        {
            _ = Context.JavaHomeLocator(major);
        }
        catch (JavaNotFoundException)
        {
            bad = true;
        }

        if (bad)
        {
            var manifest = await mojangService.GetRuntimeManifestAsync().ConfigureAwait(false);
            var osString = GenerateOsString();
            var runtimeString = GenerateRuntimeString(major);
            if (manifest.TryGetValue(osString, out var runtimes)
             && runtimes.TryGetValue(runtimeString, out var runtime))
            {
                var first = runtime.FirstOrDefault();
                if (first != null)
                {
                    var entries = new List<BundledRuntime.File>();
                    var links = new List<BundledRuntime.Link>();
                    using var client = httpClientFactory.CreateClient();
                    var content = await client.GetStringAsync(first.Manifest.Url, token).ConfigureAwait(false);
                    var json = JsonSerializer.Deserialize<JsonObject>(content, JsonSerializerOptions.Default);
                    if (json is not null && json.TryGetPropertyValue("files", out var v) && v is JsonObject files)
                    {
                        foreach (var (path, value) in files)
                        {
                            if (value is JsonObject file)
                            {
                                var type = file["type"]?.GetValue<string>()
                                        ?? throw new FormatException("Invalid type property");

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
                                            entries.Add(new(path, url, sha1, executable));
                                        }
                                        else
                                        {
                                            throw new FormatException("Invalid downloads property");
                                        }

                                        break;
                                    }
                                    case "link":
                                    {
                                        // 似乎 Target 都在 ../java.base/ 也就是 Runtime 目录之外，而且还互相冲突，那就不处理了
                                        // var target = file["target"]?.GetValue<string>()
                                        //           ?? throw new FormatException("Invalid target property");
                                        // links.Add(new(path, target));

                                        break;
                                    }
                                }
                            }
                            else
                            {
                                throw new FormatException("Invalid file entry");
                            }
                        }
                    }
                    // 如果获取不到也什么都不做，悄咪咪把错误留给 Launch Flow 再爆出来

                    Context.Runtime = new(major, entries, links);
                }
            }
        }

        Context.IsRuntimeEnsured = true;
    }

    private static string GenerateRuntimeString(uint major) =>
        major switch
        {
            8 => "jre-legacy",
            11 or 16 => "java-runtime-alpha",
            17 => "java-runtime-beta",
            21 => "java-runtime-delta",
            24 or 25 => "java-runtime-epsilon",
            _ => "java-runtime-gamma"
        };

    private static string GenerateOsString()
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

        throw new NotSupportedException("Unsupported operating system.");
    }
}
