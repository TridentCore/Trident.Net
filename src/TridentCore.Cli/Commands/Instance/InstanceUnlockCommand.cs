using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Instance;

public class InstanceUnlockCommand(
    InstanceContextResolver resolver,
    ProfileManager profileManager,
    CliOutput output
) : InstanceCommandBase<InstanceUnlockCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var instance = ResolveInstance(settings);
        var result = InstanceOperation.Unlock(resolver, profileManager, instance.Key, settings.Profile);

        if (output.UseStructuredOutput)
        {
            output.WriteData(new { action = "unlock", key = result.Key, oldSource = result.OldSource });
        }
        else
        {
            output.WriteKeyValueTable(
                "Instance unlocked",
                ("Instance", result.Key),
                ("Old Source", result.OldSource),
                ("New Source", (string?)null)
            );
            output.WriteSuccess($"Instance {result.Key} unlocked.");
        }

        return ExitCodes.SUCCESS;
    }

    public class Arguments : InstanceArgumentsBase { }
}
