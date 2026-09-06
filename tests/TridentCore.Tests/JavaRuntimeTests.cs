using Microsoft.VisualStudio.TestTools.UnitTesting;
using TridentCore.Abstractions;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;
using TridentCore.Tests.Utilities;

namespace TridentCore.Tests;

[TestClass]
public sealed class JavaRuntimeTests
{
    [TestMethod]
    public void ProbeReadsActualJavaArchitectureAndMajor()
    {
        var info = JavaHelper.ParseRuntimeInfoFromOutput("""
            Property settings:
                java.vendor = Example
                java.version = 21.0.8
                os.arch = aarch64
            openjdk version "21.0.8"
            """);
        Assert.IsNotNull(info);
        Assert.AreEqual(21, info.Value.Major);
        Assert.AreEqual("arm64", info.Value.Architecture);
    }

    [TestMethod]
    public async Task ForcedJavaUsesItsActualMajorOutsideComponentRequirements()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Java probe fixture uses a POSIX executable");
            return;
        }
        using var sandbox = FixtureHelper.CreateSandbox();
        var home = Path.Combine(sandbox.Root, "java");
        Directory.CreateDirectory(Path.Combine(home, "bin"));
        var executable = Path.Combine(home, "bin", "java");
        await File.WriteAllTextAsync(executable, "#!/bin/sh\nprintf 'java.version = 23.0.1\\nos.arch = aarch64\\n' >&2\n");
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var java = await JavaHelper.MakeForcedLocator(home)([17u, 21u], CancellationToken.None);
        Assert.AreEqual(JavaHelper.JavaResolution.Source.Forced, java.Origin);
        Assert.AreEqual(23u, java.Major);
        var result = new LaunchCompilerService().Compile(FixtureHelper.Resolution(FixtureHelper.Game()),
            FixtureHelper.Target with { JavaMajor = java.Major, Architecture = java.Architecture },
            ignoreJavaRequirements: true).Result;
        Assert.AreEqual(23u, result.Target.JavaMajor);
    }

    [TestMethod]
    public async Task BundledJavaMustMatchItsCacheDirectoryMajor()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Java probe fixture uses a POSIX executable");
            return;
        }
        var wrong = JavaHelper.BundledHome(21);
        var correct = JavaHelper.BundledHome(17);
        foreach (var home in new[] { wrong, correct })
        {
            Assert.IsFalse(Directory.Exists(home));
            Directory.CreateDirectory(Path.Combine(home, "bin"));
            var java = Path.Combine(home, "bin", "java");
            await File.WriteAllTextAsync(java, "#!/bin/sh\nprintf 'java.version = 17.0.1\\nos.arch = aarch64\\n' >&2\n");
            File.SetUnixFileMode(java, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        try
        {
            var result = await JavaHelper.MakeLocator(_ => null)([21u, 17u], CancellationToken.None);
            Assert.AreEqual(correct, result.Home);
            Assert.AreEqual(17u, result.Major);
            Assert.AreEqual("arm64", result.Architecture);
        }
        finally
        {
            Directory.Delete(PathDef.Default.DirectoryOfRuntime(21), true);
            Directory.Delete(PathDef.Default.DirectoryOfRuntime(17), true);
        }
    }
}
