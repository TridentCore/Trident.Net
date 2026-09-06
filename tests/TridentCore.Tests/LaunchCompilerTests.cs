using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TridentCore.Abstractions.Launching;
using TridentCore.Core.Engines.Deploying;
using TridentCore.Core.Engines.Deploying.Stages;
using TridentCore.Core.Extensions;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;
using TridentCore.Tests.Utilities;

namespace TridentCore.Tests;

[TestClass]
public sealed class LaunchCompilerTests
{
    [TestMethod]
    public async Task RepeatedDeploymentReusesResultWithoutAppendingArguments()
    {
        var resolution = FixtureHelper.Resolution(FixtureHelper.Game() with
        {
            JavaArguments = new() { Append = [new("-Dcustom=true")] }
        });
        var compiler = new LaunchCompilerService();
        var java = new JavaHelper.JavaResolution("unused", JavaHelper.JavaResolution.Source.UserConfigured, 17, "arm64");
        var target = new LaunchTarget(PlatformHelper.GetOsName(), java.Architecture, PlatformHelper.GetOsVersion(), java.Major);
        var cached = compiler.Compile(resolution, target).Result;
        var context = new DeployContext("fixture", new() { Version = "1" }, null!, new(),
            new(null, new Dictionary<string, LaunchDefinitionSnapshot.Entry>(), "empty"), (_, _) => Task.FromResult(java))
        {
            Resolution = resolution, Java = java,
            Lock = new() { Platform = new("1", null) },
            BaseLock = new() { Platform = new("1", null), Launch = cached }
        };
        using var stage = new CompileLaunchStage(compiler, NullLogger<CompileLaunchStage>.Instance) { Context = context };
        await stage.ProcessAsync(CancellationToken.None);
        Assert.AreSame(cached, context.Lock.Launch);
        Assert.AreEqual(1, context.Lock.Launch!.JavaArguments.Count(x => x == "-Dcustom=true"));
    }

    [TestMethod]
    public void CachedArgumentsKeepArtifactIdentitiesInsteadOfAbsolutePaths()
    {
        var artifact = FixtureHelper.Artifact("group:agent:1");
        var component = FixtureHelper.Game() with
        {
            Libraries = [new(artifact, LaunchLibrary.Usage.Agent)],
            JavaArguments = new() { Append = [new("-Dfile=${artifact:group:agent:1}")] }
        };
        var result = new LaunchCompilerService().Compile(FixtureHelper.Resolution(component), FixtureHelper.Target).Result;
        Assert.IsTrue(result.JavaArguments.Contains("-Dfile=${artifact:group:agent:1}"));
        Assert.IsTrue(result.JavaArguments.Contains("-javaagent:${artifact:group:agent:1}"));
        Assert.IsFalse(result.MakeIgniter().JvmArguments.Any(x => x.Contains("${artifact:")));
    }

    [TestMethod]
    public void TargetArchitectureInvalidatesCompiledCache()
    {
        var compiler = new LaunchCompilerService();
        var resolution = FixtureHelper.Resolution(FixtureHelper.Game());
        Assert.AreNotEqual(compiler.Fingerprint(resolution, FixtureHelper.Target),
            compiler.Fingerprint(resolution, FixtureHelper.Target with { Architecture = "x64" }));
    }

    [TestMethod]
    public void ArgumentExpansionHandlesEveryPlaceholderAndEmptyValues()
    {
        var component = FixtureHelper.Game() with
        {
            JavaArguments = new() { Append = [new("-Dpaths=${library_directory}:${natives_directory}")] },
            GameArguments = new() { Replace = [new("--name=${auth_player_name}"), new("")] }
        };
        var result = new LaunchCompilerService().Compile(FixtureHelper.Resolution(component), FixtureHelper.Target).Result;
        var igniter = result.MakeIgniter().SetJavaHome("/java").SetWorkingDirectory("/game").SetMaxMemory(1024)
            .SetLibraryRootDirectory("/libraries").SetNativesRootDirectory("/natives").SetUserName("Player");
        using var process = igniter.Build();
        Assert.IsTrue(process.StartInfo.ArgumentList.Contains("-Dpaths=/libraries:/natives"));
        Assert.IsTrue(process.StartInfo.ArgumentList.Contains("--name=Player"));
        Assert.AreEqual("", process.StartInfo.ArgumentList.Last());
    }
}
