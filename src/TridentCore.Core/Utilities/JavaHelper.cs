using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using TridentCore.Abstractions;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Exceptions;
using TridentCore.Core.Services.Instances;

namespace TridentCore.Core.Utilities;

public static class JavaHelper
{
    private static readonly string[] WINDOWS_REGISTRY_JAVA_ROOTS =
    [
        @"SOFTWARE\JavaSoft\Java Runtime Environment",
        @"SOFTWARE\JavaSoft\JRE",
        @"SOFTWARE\JavaSoft\Java Development Kit",
        @"SOFTWARE\JavaSoft\JDK",
        @"SOFTWARE\Eclipse Adoptium\JDK",
        @"SOFTWARE\Eclipse Foundation\JDK",
        @"SOFTWARE\AdoptOpenJDK\JDK",
        @"SOFTWARE\Microsoft\JDK"
    ];

    private static readonly string[] WINDOWS_JAVA_HOME_VALUE_NAMES =
    [
        "JavaHome", "InstallationPath", "InstallLocation", "InstallDir", "Home", "Path"
    ];

    public static JavaHomeLocatorDelegate MakeLocator(Func<uint, string?> javaHomeSelector, bool withFallback = true) =>
        (majors, token) => LocateAsync(majors, javaHomeSelector, withFallback, token);

    public static JavaHomeLocatorDelegate MakeForcedLocator(string javaHome) => async (_, token) =>
    {
        var info = await ProbeHomeAsync(javaHome, cancellationToken: token).ConfigureAwait(false);
        if (info is not { Major: > 0, Architecture: not null })
        {
            throw new InvalidOperationException($"Configured Java installation '{javaHome}' is unavailable or could not be inspected");
        }

        return new(Path.GetFullPath(javaHome), JavaResolution.Source.Forced,
                   (uint)info.Value.Major.Value, info.Value.Architecture!);
    };

    public static async Task<IReadOnlyList<JavaRuntimeCandidate>> ScanJavaRuntimesAsync(
        CancellationToken cancellationToken = default)
    {
        var raw = OperatingSystem.IsWindows()
                      ? DiscoverJavaRuntimesWindows()
                      :
                      OperatingSystem.IsMacOS()
                          ?
                          await DiscoverJavaRuntimesMacOsAsync(cancellationToken).ConfigureAwait(false)
                          : [];

        if (raw.Count == 0)
        {
            return [];
        }

        var results = new ConcurrentBag<JavaRuntimeCandidate>();
        await Task
             .WhenAll(raw.Select(item => ProbeAndBuildCandidateAsync(item.Home,
                                                                     item.Vendor,
                                                                     item.Version,
                                                                     item.Source,
                                                                     results,
                                                                     cancellationToken)))
             .ConfigureAwait(false);
        return SortJavaRuntimeCandidates(results);
    }

    private static async Task ProbeAndBuildCandidateAsync(
        string home,
        string? vendor,
        string? version,
        string source,
        ConcurrentBag<JavaRuntimeCandidate> results,
        CancellationToken cancellationToken)
    {
        var info = await ProbeHomeAsync(home, cancellationToken: cancellationToken).ConfigureAwait(false);
        results.Add(new(home,
                        info?.Vendor ?? vendor,
                        info?.Version ?? version,
                        info?.Major ?? ParseJavaMajor(version),
                        source));
    }

    public static async Task<JavaRuntimeInfo?> ProbeHomeAsync(
        string home,
        int timeoutMilliseconds = 5000,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var path = ResolveJavaExecutable(home);
            if (path == null)
            {
                return null;
            }

            var output =
                await RunAndCaptureAsync(path,
                                         timeoutMilliseconds,
                                         cancellationToken,
                                         home,
                                         "-XshowSettings:properties",
                                         "-version")
                   .ConfigureAwait(false)
             ?? await RunAndCaptureAsync(path, timeoutMilliseconds, cancellationToken, home, "-version")
                   .ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(output) ? null : ParseRuntimeInfoFromOutput(output);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    internal static JavaRuntimeInfo? ParseRuntimeInfoFromOutput(string output)
    {
        var vendor = ExtractJavaProperty(output, "java.vendor");
        var version = ExtractJavaProperty(output, "java.version") ?? ExtractJavaVersion(output);
        var major = ParseJavaMajor(version);
        var architecture = ExtractJavaProperty(output, "os.arch");
        return vendor == null && version == null && major == null ? null
            : new(vendor, version, major, architecture is null ? null : LaunchRuleHelper.NormalizeArchitecture(architecture));
    }

    private static async Task<JavaResolution> LocateAsync(
        IReadOnlyList<uint> majors,
        Func<uint, string?> selector,
        bool withFallback,
        CancellationToken token)
    {
        if (majors.Count == 0) throw new ArgumentException("No compatible Java versions supplied", nameof(majors));
        var configured = majors.Select(selector).Where(x => !string.IsNullOrWhiteSpace(x))
                               .Select(x => x!).Distinct(FileHelper.PathComparer).ToArray();
        foreach (var home in configured)
        {
            var info = await ProbeHomeAsync(home, cancellationToken: token).ConfigureAwait(false);
            if (info is { Major: > 0, Architecture: not null } && majors.Contains((uint)info.Value.Major.Value))
            {
                return new(Path.GetFullPath(home), JavaResolution.Source.UserConfigured,
                           (uint)info.Value.Major.Value, info.Value.Architecture!);
            }
        }
        if (configured.Length > 0)
        {
            throw new InvalidOperationException($"Configured Java installations are unavailable or incompatible with Java {string.Join(", ", majors)}");
        }
        if (withFallback)
        {
            foreach (var major in majors)
            {
                var home = BundledHome(major);
                if (ResolveJavaExecutable(home) is null) continue;
                var info = await ProbeHomeAsync(home, cancellationToken: token).ConfigureAwait(false);
                if (info is { Major: > 0, Architecture: not null } && info.Value.Major.Value == major)
                {
                    return new(home, JavaResolution.Source.Bundled, major, info.Value.Architecture!);
                }
            }
        }
        throw new JavaNotFoundException(majors[0]);
    }

    public static string BundledHome(uint major) => OperatingSystem.IsMacOS()
        ? Path.Combine(PathDef.Default.DirectoryOfRuntime(major), "jre.bundle", "Contents", "Home")
        : PathDef.Default.DirectoryOfRuntime(major);

    private static string? ResolveJavaExecutable(string home)
    {
        var candidates = OperatingSystem.IsWindows()
                             ? new[] { Path.Combine(home, "bin", "java.exe"), Path.Combine(home, "bin", "java") }
                             : new[] { Path.Combine(home, "bin", "java"), Path.Combine(home, "bin", "java.exe") };

        return candidates.FirstOrDefault(File.Exists);
    }

    [SupportedOSPlatform("windows")]
    private static RegistryKey? OpenBaseKey(RegistryHive hive, RegistryView view)
    {
        try
        {
            return RegistryKey.OpenBaseKey(hive, view);
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractQuotedSegment(string value, int index)
    {
        var start = value.IndexOf('"', index);
        if (start < 0)
        {
            return null;
        }

        var end = value.IndexOf('"', start + 1);
        return end > start ? value[(start + 1)..end] : null;
    }

    private static string? NormalizeJavaHome(string? home)
    {
        if (string.IsNullOrWhiteSpace(home))
        {
            return null;
        }

        home = home.Trim().Trim('"');
        if (File.Exists(home))
        {
            home = Path.GetDirectoryName(Path.GetDirectoryName(home)) ?? home;
        }
        else if (Directory.Exists(home)
              && string.Equals(Path.GetFileName(home), "bin", StringComparison.OrdinalIgnoreCase))
        {
            home = Path.GetDirectoryName(home) ?? home;
        }

        if (!Directory.Exists(home) || ResolveJavaExecutable(home) == null)
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(home).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return home.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static IReadOnlyList<JavaRuntimeCandidate> SortJavaRuntimeCandidates(
        IEnumerable<JavaRuntimeCandidate> candidates) =>
    [
        .. candidates
          .OrderByDescending(x => x.Major ?? 0)
          .ThenBy(x => x.Vendor ?? string.Empty, StringComparer.OrdinalIgnoreCase)
          .ThenBy(x => x.Version ?? string.Empty, StringComparer.OrdinalIgnoreCase)
          .ThenBy(x => x.Home, StringComparer.OrdinalIgnoreCase)
    ];

    private static async Task<string?> RunAndCaptureAsync(
        string executable,
        int timeoutMilliseconds,
        CancellationToken cancellationToken,
        string? workingDirectory = null,
        params string[] arguments)
    {
        using var process = new Process { StartInfo = BuildStartInfo(executable, arguments, workingDirectory) };

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMilliseconds);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            return string.Join(Environment.NewLine,
                               await stdoutTask.ConfigureAwait(false),
                               await stderrTask.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);

            try
            {
                await process.WaitForExitAsync().ConfigureAwait(false);
                await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            }
            catch
            {
            }

            throw;
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch
        {
        }
    }

    private static ProcessStartInfo BuildStartInfo(
        string executable,
        IEnumerable<string> arguments,
        string? workingDirectory = null)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (workingDirectory != null)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static string? ExtractJavaProperty(string output, string propertyName)
    {
        using var reader = new StringReader(output);
        var prefix = $"{propertyName} = ";

        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                return trimmed[prefix.Length..];
            }
        }

        return null;
    }

    private static string? ExtractJavaVersion(string output)
    {
        using var reader = new StringReader(output);

        while (reader.ReadLine() is { } line)
        {
            var marker = line.IndexOf("version \"", StringComparison.OrdinalIgnoreCase);
            if (marker < 0)
            {
                continue;
            }

            var start = marker + "version \"".Length;
            var end = line.IndexOf('"', start);
            if (end > start)
            {
                return line[start..end];
            }
        }

        return null;
    }

    internal static int? ParseJavaMajor(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        var value = version.AsSpan().Trim();
        if (value.StartsWith("1.")) value = value[2..];
        var length = 0;
        while (length < value.Length && char.IsAsciiDigit(value[length])) length++;
        return int.TryParse(value[..length], out var result) && result > 0 ? result : null;
    }

    public readonly record struct JavaRuntimeCandidate(
        string Home,
        string? Vendor,
        string? Version,
        int? Major,
        string Source);

    public readonly record struct JavaRuntimeInfo(string? Vendor, string? Version, int? Major, string? Architecture);

    // 定位到的 Java home 与其来源配对，部署管线据此区分用户配置的 JRE（不动）
    // 与捆绑运行时（自愈）。返回裸路径会让每个调用方从字符串猜来源。
    public record JavaResolution(string Home, JavaResolution.Source Origin, uint Major, string Architecture)
    {
        public enum Source
        {
            // 实例级 Java 路径或命令行路径，跳过组件 major 匹配与运行时下载。
            Forced,

            // 全局按 major 提供的运行时，仅验证，不由部署管线修改。
            UserConfigured,

            // 无可用用户配置——解析到 runtimes/ 下的管线捆绑运行时。
            Bundled
        }
    }

    #region Discovery

    private static List<(string Home, string? Vendor, string? Version, string Source)> DiscoverJavaRuntimesWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<(string, string?, string?, string)>();

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var baseKey = OpenBaseKey(hive, view);
                if (baseKey == null)
                {
                    continue;
                }

                foreach (var rootPath in WINDOWS_REGISTRY_JAVA_ROOTS)
                {
                    using var root = baseKey.OpenSubKey(rootPath);
                    if (root == null)
                    {
                        continue;
                    }

                    CollectWindowsRegistryHomes(results, seen, root, rootPath);
                    if (root.GetValue("CurrentVersion") is string currentVersion)
                    {
                        CollectWindowsRegistrySubHomes(results, seen, root, rootPath, currentVersion);
                    }

                    foreach (var subKeyName in root.GetSubKeyNames())
                    {
                        CollectWindowsRegistrySubHomes(results, seen, root, rootPath, subKeyName);
                    }
                }
            }
        }

        return results;
    }

    [SupportedOSPlatform("windows")]
    private static void CollectWindowsRegistryHomes(
        List<(string, string?, string?, string)> results,
        HashSet<string> seen,
        RegistryKey key,
        string source,
        string? version = null)
    {
        foreach (var valueName in WINDOWS_JAVA_HOME_VALUE_NAMES)
        {
            if (key.GetValue(valueName) is not string rawHome)
            {
                continue;
            }

            var home = NormalizeJavaHome(rawHome);
            if (home != null && seen.Add(home))
            {
                results.Add((home, null, version, $@"Registry: {source}"));
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void CollectWindowsRegistrySubHomes(
        List<(string, string?, string?, string)> results,
        HashSet<string> seen,
        RegistryKey root,
        string rootPath,
        string subKeyName)
    {
        using var subKey = root.OpenSubKey(subKeyName);
        if (subKey == null)
        {
            return;
        }

        CollectWindowsRegistryHomes(results, seen, subKey, $@"{rootPath}\{subKeyName}", subKeyName);
    }

    private static async Task<List<(string Home, string? Vendor, string? Version, string Source)>>
        DiscoverJavaRuntimesMacOsAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return [];
        }

        const string javaHomeTool = "/usr/libexec/java_home";
        if (!File.Exists(javaHomeTool))
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<(string, string?, string?, string)>();

        var output = await RunAndCaptureAsync(javaHomeTool, 5000, cancellationToken, null, "-V").ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(output))
        {
            using var reader = new StringReader(output);
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                CollectMacOsJavaHomeLine(results, seen, line);
            }
        }

        if (results.Count == 0)
        {
            var defaultOutput = await RunAndCaptureAsync(javaHomeTool, 5000, cancellationToken).ConfigureAwait(false);
            var defaultHome = defaultOutput?.Trim();
            if (defaultHome != null)
            {
                var home = NormalizeJavaHome(defaultHome);
                if (home != null && seen.Add(home))
                {
                    results.Add((home, null, null, "java_home"));
                }
            }
        }

        return results;
    }

    private static void CollectMacOsJavaHomeLine(
        List<(string, string?, string?, string)> results,
        HashSet<string> seen,
        string line)
    {
        var trimmed = line.Trim();
        var pathStart = trimmed.LastIndexOf(" /", StringComparison.Ordinal);
        if (pathStart < 0)
        {
            return;
        }

        var rawHome = trimmed[(pathStart + 1)..].Trim();
        var home = NormalizeJavaHome(rawHome);
        if (home == null || !seen.Add(home))
        {
            return;
        }

        var version = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var vendor = ExtractQuotedSegment(trimmed, 0);
        results.Add((home, vendor, version, "java_home"));
    }

    #endregion
}
