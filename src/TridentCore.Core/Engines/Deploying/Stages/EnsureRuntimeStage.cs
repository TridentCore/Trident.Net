using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        Context.Lock = Context.Lock with { Runtime = Context.Lock.Runtime };

        // Only the bundled runtime (or a not-yet-installed one) is pipeline-managed and eligible
        // for manifest self-healing. A user-configured JRE is left entirely to the user environment.
        bool needManifest;
        try
        {
            var resolution = Context.JavaHomeLocator(major);
            needManifest = resolution.Origin == JavaHelper.JavaResolution.Source.Bundled;
        }
        catch (JavaNotFoundException)
        {
            needManifest = true;
        }

        if (!needManifest)
        {
            return;
        }

        var content = await LoadManifestAsync(major, token).ConfigureAwait(false);
        if (content is null)
        {
            return;
        }

        var (files, links) = ParseRuntimeFiles(content);
        if (files.Count == 0)
        {
            logger.LogWarning("Java runtime manifest for Java {major} contained no downloadable files; it will not be auto-installed and launch will fail with JavaNotFoundException",
                              major);
        }

        Context.Runtime = new(major, files, links);
    }

    // Resolves the per-major runtime manifest JSON. Returns the cached copy at
    // runtimes/{major}.json when its sha1 matches the fingerprint recorded in the lock
    // (offline fast path); otherwise fetches Mojang's runtime index, downloads the manifest,
    // verifies it against the index sha1, persists it, and records the fingerprint for next time.
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
        var osString = GenerateOsString();
        var runtimeString = GenerateRuntimeString(major);
        if (!manifest.TryGetValue(osString, out var runtimes)
         || !runtimes.TryGetValue(runtimeString, out var runtime)
         || runtime.Count == 0)
        {
            logger.LogWarning("Java runtime {runtime} unavailable for platform {os}; Java {major} will not be auto-installed and launch will fail with JavaNotFoundException",
                              runtimeString,
                              osString,
                              major);
            return null;
        }

        var first = runtime[0];
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

        File.Move(tmp, path, overwrite: true);

        Context.Lock = Context.Lock with { Runtime = new(major, first.Manifest.Sha1) };

        return await File.ReadAllTextAsync(path, token).ConfigureAwait(false);
    }

    private static ( List<BundledRuntime.File> Files, List<BundledRuntime.Link> Links ) ParseRuntimeFiles(
        string content)
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
                        // Targets resolve outside the runtime directory and conflict with each other, so links are ignored.
                        break;
                    }
                }
            }
        }

        return (files, links);
    }

    private static string GenerateRuntimeString(uint major) =>
        major switch
        {
            8 => "jre-legacy",
            11 or 16 => "java-runtime-alpha",
            17 => "java-runtime-beta",
            21 => "java-runtime-delta",
            24 or 25 => "java-runtime-epsilon",
            _ => "java-runtime-gamma",
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
