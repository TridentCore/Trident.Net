using Spectre.Console.Cli;
using Trident.Cli.Services;
using Trident.Core.Services;
using Trident.Core.Services.Instances;
using Trident.Core.Utilities;

namespace Trident.Cli.Commands.Instance;

public class InstanceBuildCommand(
    InstanceContextResolver resolver,
    InstanceManager instanceManager,
    TrackerAwaiter trackerAwaiter,
    CliOutput output
) : InstanceCommandBase<InstanceBuildCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        BuildAsync(settings, cancellationToken).GetAwaiter().GetResult();
        return ExitCodes.Success;
    }

    private async Task BuildAsync(Arguments settings, CancellationToken cancellationToken)
    {
        var instance = ResolveInstance(settings);
        var options = new DeployOptions(
            settings.FastMode,
            settings.ResolveDependency,
            settings.FullCheck
        );
        var locator = JavaHelper.MakeLocator(_ => settings.JavaHome, true);
        var tracker = instanceManager.Deploy(instance.Key, options, locator);
        await trackerAwaiter.AwaitDeployAsync(tracker, cancellationToken).ConfigureAwait(false);

        var result = new { action = "build", key = instance.Key, state = "finished" };
        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
        }
        else
        {
            output.WriteMessage($"Instance {instance.Key} built.");
        }
    }

    public class Arguments : InstanceArgumentsBase
    {
        [CommandOption("--fast")]
        public bool? FastMode { get; set; }

        [CommandOption("--resolve-dependency")]
        public bool? ResolveDependency { get; set; }

        [CommandOption("--full-check")]
        public bool? FullCheck { get; set; }

        [CommandOption("--java-home <PATH>")]
        public string? JavaHome { get; set; }
    }
}
