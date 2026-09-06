using Microsoft.Extensions.Logging;
using TridentCore.Abstractions.Launching;
using TridentCore.Core.Services;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Engines.Deploying.Stages;

public sealed class CompileLaunchStage(LaunchCompilerService compiler, ILogger<CompileLaunchStage> logger) : StageBase
{
    protected override Task OnProcessAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var target = new LaunchTarget(PlatformHelper.GetOsName(), Context.Java.Architecture,
                                      PlatformHelper.GetOsVersion(), Context.Java.Major);
        var fingerprint = compiler.Fingerprint(Context.Resolution, target);
        if (Context.BaseLock?.Launch is { } cached && cached.Fingerprint == fingerprint)
        {
            Context.Lock = Context.Lock with { Launch = cached };
            logger.LogInformation("Reusing compiled launch for {key}", Context.Key);
            return Task.CompletedTask;
        }
        var compilation = compiler.Compile(Context.Resolution, target,
                                            ignoreJavaRequirements: Context.Java.Origin == JavaHelper.JavaResolution.Source.Forced);
        Context.Lock = Context.Lock with { Launch = compilation.Result };
        foreach (var diagnostic in compilation.Diagnostics)
        {
            logger.LogWarning("{source}: {message}", diagnostic.Path, diagnostic.Message);
        }
        return Task.CompletedTask;
    }
}
