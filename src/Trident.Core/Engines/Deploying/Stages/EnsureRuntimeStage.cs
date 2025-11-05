using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trident.Core.Exceptions;
using Trident.Core.Services;
using Trident.Core.Utilities;

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
            // var manifest = await prismLauncherService.GetRuntimeAsync(major, token).ConfigureAwait(false);
            // var osString = $"{PlatformHelper.GetOsName()}-{PlatformHelper.GetOsArch()}";
            // var runtime = manifest.Runtimes.OrderBy(x => x.ReleaseTime).FirstOrDefault(x => x.RuntimeOS == osString);
            // if (runtime != null)
            // {
            //     Context.Runtime = new(major, runtime.Vendor, runtime.Url, true);
            // }

            // 如果获取不到也什么都不做，悄咪咪把错误留给 Launch Flow 再爆出来

            var manifest = await mojangService.GetRuntimeManifestAsync().ConfigureAwait(false);
            var osString = GenerateOsString();
            var runtimeString = GenerateRuntimeString(major);
            if (manifest.TryGetValue(osString, out var runtimes)
             && runtimes.TryGetValue(runtimeString, out var runtime))
            {
                var first = runtime.FirstOrDefault();
                if (first != null)
                {
                    var entries = new List<BundledRuntime.Entry>();
                    using var client = httpClientFactory.CreateClient();
                    var content = await client.GetStringAsync(first.Manifest.Url, token).ConfigureAwait(false);
                    var json = JsonSerializer.Deserialize<JsonObject>(content, JsonSerializerOptions.Default);
                    if (json is not null && json.TryGetPropertyValue("files", out var v) && v is JsonObject files)
                    {
                        foreach (var (path, value) in files)
                        {
                            if (value is JsonObject file)
                            {
                                if (file.TryGetPropertyValue("type", out var type)
                                 && type != null
                                 && type.GetValue<string>() == "directory")
                                {
                                    continue;
                                }

                                if (file.TryGetPropertyValue("downloads", out var d)
                                 && d is JsonObject downloads
                                 && downloads.TryGetPropertyValue("raw", out var r)
                                 && r is JsonObject raw)
                                {
                                    var executable = file["executable"]?.GetValue<bool>() ?? false;
                                    var sha1 = raw["sha1"]?.GetValue<string>()
                                            ?? throw new FormatException("Invalid file entry");
                                    var urlString = raw["url"]?.GetValue<string>()
                                                 ?? throw new FormatException("Invalid file entry");
                                    var url = Uri.IsWellFormedUriString(urlString, UriKind.Absolute)
                                                  ? new Uri(urlString)
                                                  : throw new FormatException("Invalid url string");
                                    entries.Add(new(path, url, sha1, executable));
                                    continue;
                                }
                            }

                            throw new FormatException("Invalid file entry");
                        }
                    }

                    Context.Runtime = new(major, entries);
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
            else if (RuntimeInformation.OSArchitecture == Architecture.X86)
            {
                return "windows-x86";
            }
            else if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
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
            else if (RuntimeInformation.OSArchitecture == Architecture.X86)
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
            else if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
            {
                return "mac-os-arm64";
            }
        }

        throw new NotSupportedException("Unsupported operating system.");
    }
}
