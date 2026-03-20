using System.Diagnostics;
using Trident.Abstractions;
using Trident.Core.Exceptions;
using Trident.Core.Services.Instances;

namespace Trident.Core.Utilities;

public static class JavaHelper
{
    public static JavaHomeLocatorDelegate MakeLocator(
        Func<uint, string?> javaHomeSelector,
        bool withFallback = true
    ) => major => Locate(major, javaHomeSelector(major), withFallback);

    public static JavaRuntimeInfo? ProbeHome(string home, int timeoutMilliseconds = 5000)
    {
        try
        {
            var path = ResolveJavaExecutable(home);
            if (path == null)
            {
                return null;
            }

            var output = RunJavaAndCaptureMetadata(path, home, timeoutMilliseconds, "-XshowSettings:properties", "-version")
                ?? RunJavaAndCaptureMetadata(path, home, timeoutMilliseconds, "-version");
            if (string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            var vendor = ExtractJavaProperty(output, "java.vendor");
            var version = ExtractJavaProperty(output, "java.version") ?? ExtractJavaVersion(output);
            var major = ParseJavaMajor(version);
            return vendor == null && version == null && major == null ? null : new(vendor, version, major);
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
        using var process = new Process();
        process.StartInfo = new(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

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
