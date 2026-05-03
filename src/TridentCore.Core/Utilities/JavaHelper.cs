using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using TridentCore.Abstractions;
using TridentCore.Core.Exceptions;
using TridentCore.Core.Services.Instances;

namespace TridentCore.Core.Utilities;

public static class JavaHelper
{
    private static readonly string[] WindowsRegistryJavaRoots =
    [
        @"SOFTWARE\JavaSoft\Java Runtime Environment",
        @"SOFTWARE\JavaSoft\JRE",
        @"SOFTWARE\JavaSoft\Java Development Kit",
        @"SOFTWARE\JavaSoft\JDK",
        @"SOFTWARE\Eclipse Adoptium\JDK",
        @"SOFTWARE\Eclipse Foundation\JDK",
        @"SOFTWARE\AdoptOpenJDK\JDK",
        @"SOFTWARE\Microsoft\JDK",
    ];

    private static readonly string[] WindowsJavaHomeValueNames =
    [
        "JavaHome",
        "InstallationPath",
        "InstallLocation",
        "InstallDir",
        "Home",
        "Path",
    ];

    public static JavaHomeLocatorDelegate MakeLocator(
        Func<uint, string?> javaHomeSelector,
        bool withFallback = true
    ) => major => Locate(major, javaHomeSelector(major), withFallback);

    public static async Task<IReadOnlyList<JavaRuntimeCandidate>> ScanJavaRuntimesAsync(
        CancellationToken cancellationToken = default
    )
    {
        var raw =
            OperatingSystem.IsWindows() ? DiscoverJavaRuntimesWindows()
            : OperatingSystem.IsMacOS()
                ? await DiscoverJavaRuntimesMacOsAsync(cancellationToken).ConfigureAwait(false)
            : [];

        if (raw.Count == 0)
        {
            return [];
        }

        var results = new ConcurrentBag<JavaRuntimeCandidate>();
        await Task.WhenAll(
                raw.Select(item =>
                    ProbeAndBuildCandidateAsync(
                        item.Home,
                        item.Vendor,
                        item.Version,
                        item.Source,
                        results,
                        cancellationToken
                    )
                )
            )
            .ConfigureAwait(false);
        return SortJavaRuntimeCandidates(results);
    }

    private static async Task ProbeAndBuildCandidateAsync(
        string home,
        string? vendor,
        string? version,
        string source,
        ConcurrentBag<JavaRuntimeCandidate> results,
        CancellationToken cancellationToken
    )
    {
        var info = await ProbeHomeAsync(home, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        results.Add(
            new(
                home,
                info?.Vendor ?? vendor,
                info?.Version ?? version,
                info?.Major ?? ParseJavaMajor(version),
                source
            )
        );
    }

    #region Discovery

    private static List<(
        string Home,
        string? Vendor,
        string? Version,
        string Source
    )> DiscoverJavaRuntimesWindows()
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

                foreach (var rootPath in WindowsRegistryJavaRoots)
                {
                    using var root = baseKey.OpenSubKey(rootPath);
                    if (root == null)
                    {
                        continue;
                    }

                    CollectWindowsRegistryHomes(results, seen, root, rootPath);
                    if (root.GetValue("CurrentVersion") is string currentVersion)
                    {
                        CollectWindowsRegistrySubHomes(
                            results,
                            seen,
                            root,
                            rootPath,
                            currentVersion
                        );
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
        string? version = null
    )
    {
        foreach (var valueName in WindowsJavaHomeValueNames)
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
        string subKeyName
    )
    {
        using var subKey = root.OpenSubKey(subKeyName);
        if (subKey == null)
        {
            return;
        }

        CollectWindowsRegistryHomes(results, seen, subKey, $@"{rootPath}\{subKeyName}", subKeyName);
    }

    private static async Task<
        List<(string Home, string? Vendor, string? Version, string Source)>
    > DiscoverJavaRuntimesMacOsAsync(CancellationToken cancellationToken)
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

        var output = await RunAndCaptureAsync(javaHomeTool, 5000, cancellationToken, null, "-V")
            .ConfigureAwait(false);
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
            var defaultOutput = await RunAndCaptureAsync(javaHomeTool, 5000, cancellationToken)
                .ConfigureAwait(false);
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
        string line
    )
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

    public static async Task<JavaRuntimeInfo?> ProbeHomeAsync(
        string home,
        int timeoutMilliseconds = 5000,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var path = ResolveJavaExecutable(home);
            if (path == null)
            {
                return null;
            }

            var output =
                await RunAndCaptureAsync(
                        path,
                        timeoutMilliseconds,
                        cancellationToken,
                        home,
                        "-XshowSettings:properties",
                        "-version"
                    )
                    .ConfigureAwait(false)
                ?? await RunAndCaptureAsync(
                        path,
                        timeoutMilliseconds,
                        cancellationToken,
                        home,
                        "-version"
                    )
                    .ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(output) ? null : ParseRuntimeInfoFromOutput(output);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static JavaRuntimeInfo? ParseRuntimeInfoFromOutput(string output)
    {
        var vendor = ExtractJavaProperty(output, "java.vendor");
        var version = ExtractJavaProperty(output, "java.version") ?? ExtractJavaVersion(output);
        var major = ParseJavaMajor(version);
        return vendor == null && version == null && major == null
            ? null
            : new(vendor, version, major);
    }

    private static string Locate(uint major, string? home, bool withFallback = true)
    {
        if (!string.IsNullOrEmpty(home) && Directory.Exists(home))
        {
            return home;
        }

        if (withFallback)
        {
            var dir = PathDef.Default.DirectoryOfRuntime(major);
            var path = Path.Combine(dir, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
            if (File.Exists(path))
            {
                return dir;
            }
        }

        throw new JavaNotFoundException(major);
    }

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
        else if (
            Directory.Exists(home)
            && string.Equals(Path.GetFileName(home), "bin", StringComparison.OrdinalIgnoreCase)
        )
        {
            home = Path.GetDirectoryName(home) ?? home;
        }

        if (!Directory.Exists(home) || ResolveJavaExecutable(home) == null)
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(home)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return home.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static IReadOnlyList<JavaRuntimeCandidate> SortJavaRuntimeCandidates(
        IEnumerable<JavaRuntimeCandidate> candidates
    ) =>
        [
            .. candidates
                .OrderByDescending(x => x.Major ?? 0)
                .ThenBy(x => x.Vendor ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Version ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Home, StringComparer.OrdinalIgnoreCase),
        ];

    private static async Task<string?> RunAndCaptureAsync(
        string executable,
        int timeoutMilliseconds,
        CancellationToken cancellationToken,
        string? workingDirectory = null,
        params string[] arguments
    )
    {
        using var process = new Process
        {
            StartInfo = BuildStartInfo(executable, arguments, workingDirectory),
        };

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMilliseconds);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask)
                .WaitAsync(timeoutCts.Token)
                .ConfigureAwait(false);
            return string.Join(
                Environment.NewLine,
                await stdoutTask.ConfigureAwait(false),
                await stderrTask.ConfigureAwait(false)
            );
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
                // Ignore cleanup failures and preserve the original cancellation behavior.
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
            // Best-effort cleanup.
        }
    }

    private static ProcessStartInfo BuildStartInfo(
        string executable,
        IEnumerable<string> arguments,
        string? workingDirectory = null
    )
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
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

    private static int? ParseJavaMajor(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var major = version.Split('.', 2).FirstOrDefault();
        if (major != null && int.TryParse(major, out var result))
        {
            return result is 1 ? 8 : result;
        }

        return null;
    }

    public readonly record struct JavaRuntimeCandidate(
        string Home,
        string? Vendor,
        string? Version,
        int? Major,
        string Source
    );
}

public readonly record struct JavaRuntimeInfo(string? Vendor, string? Version, int? Major);
