using System.Diagnostics;
using TridentCore.Abstractions;
using TridentCore.Core.Exceptions;
using TridentCore.Core.Services.Instances;

namespace TridentCore.Core.Utilities;

public static class JavaHelper
{
    public static JavaHomeLocatorDelegate MakeLocator(
        Func<uint, string?> javaHomeSelector,
        bool withFallback = true
    ) => major => Locate(major, javaHomeSelector(major), withFallback);

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
                await RunJavaAndCaptureMetadataAsync(
                        path,
                        home,
                        timeoutMilliseconds,
                        cancellationToken,
                        "-XshowSettings:properties",
                        "-version"
                    )
                    .ConfigureAwait(false)
                ?? await RunJavaAndCaptureMetadataAsync(
                        path,
                        home,
                        timeoutMilliseconds,
                        cancellationToken,
                        "-version"
                    )
                    .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            var vendor = ExtractJavaProperty(output, "java.vendor");
            var version = ExtractJavaProperty(output, "java.version") ?? ExtractJavaVersion(output);
            var major = ParseJavaMajor(version);
            return vendor == null && version == null && major == null
                ? null
                : new(vendor, version, major);
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

    private static string? RunJavaAndCaptureMetadata(
        string executable,
        string workingDirectory,
        int timeoutMilliseconds,
        params string[] arguments
    )
    {
        using var process = new Process
        {
            StartInfo = BuildJavaMetadataStartInfo(executable, workingDirectory, arguments),
        };

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(timeoutMilliseconds))
        {
            process.Kill(true);
            process.WaitForExit();
        }

        var streamTasks = new Task[] { stdoutTask, stderrTask };
        if (!Task.WaitAll(streamTasks, timeoutMilliseconds))
        {
            return null;
        }

        return string.Join(Environment.NewLine, stdoutTask.Result, stderrTask.Result);
    }

    private static async Task<string?> RunJavaAndCaptureMetadataAsync(
        string executable,
        string workingDirectory,
        int timeoutMilliseconds,
        CancellationToken cancellationToken,
        params string[] arguments
    )
    {
        using var process = new Process
        {
            StartInfo = BuildJavaMetadataStartInfo(executable, workingDirectory, arguments),
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

    private static ProcessStartInfo BuildJavaMetadataStartInfo(
        string executable,
        string workingDirectory,
        IEnumerable<string> arguments
    )
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

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
}

public readonly record struct JavaRuntimeInfo(string? Vendor, string? Version, int? Major);
