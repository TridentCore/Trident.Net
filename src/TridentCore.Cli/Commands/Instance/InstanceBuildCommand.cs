using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Instance;

public class InstanceBuildCommand(
    InstanceContextResolver resolver,
    InstanceManager instanceManager,
    CliOutput output
) : InstanceCommandBase<InstanceBuildCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var instance = ResolveInstance(settings);
        var result = InstanceOperation.BuildAsync(
            Resolver,
            instanceManager,
            instance.Key,
            settings.Profile,
            settings.FastMode ?? false,
            settings.ResolveDependency ?? false,
            settings.FullCheck ?? false,
            settings.JavaHome
        ).GetAwaiter().GetResult();

        if (output.UseStructuredOutput)
        {
            output.WriteData(new { action = "build", key = result.Key, state = result.State });
        }
        else
        {
            output.WriteKeyValueTable(
                "Build completed",
                ("Instance", result.Key),
                ("State", result.State)
            );
            output.WriteSuccess($"Instance {result.Key} built.");
        }

        return ExitCodes.SUCCESS;
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
