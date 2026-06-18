using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Instance;

public class InstanceDeleteCommand(
    InstanceContextResolver resolver,
    ProfileManager profileManager,
    CliOutput output
) : InstanceCommandBase<InstanceDeleteCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var instance = ResolveInstance(settings);
        output.RequireConfirmation(
            $"Delete instance '{instance.Key}'? The directory will be removed on the next profile scan.",
            settings.Yes
        );

        var result = InstanceOperation.Delete(Resolver, profileManager, instance.Key, settings.Profile);

        if (output.UseStructuredOutput)
        {
            output.WriteData(new { action = "delete", key = result.Key, bomb = result.Bomb, deletedImmediately = false });
        }
        else
        {
            output.WriteKeyValueTable(
                "Instance deletion requested",
                ("Instance", result.Key),
                ("Marker", result.Bomb),
                ("Deleted Immediately", "no")
            );
            output.WriteWarning("The instance directory will be removed on the next profile scan.");
            output.WriteSuccess($"Instance {result.Key} marked for deletion.");
        }

        return ExitCodes.SUCCESS;
    }

    public class Arguments : InstanceArgumentsBase
    {
        [CommandOption("-y|--yes")]
        public bool Yes { get; set; }
    }
}
