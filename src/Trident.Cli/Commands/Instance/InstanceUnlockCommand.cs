using Spectre.Console.Cli;
using Trident.Cli.Services;
using Trident.Core.Services;

namespace Trident.Cli.Commands.Instance;

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
        var guard = profileManager.GetMutable(instance.Key);
        var oldSource = guard.Value.Setup.Source;
        guard.Value.Setup.Source = null;
        guard.DisposeAsync().AsTask().GetAwaiter().GetResult();

        var result = new { action = "unlock", key = instance.Key, oldSource };
        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
        }
        else
        {
            output.WriteMessage($"Instance {instance.Key} unlocked.");
        }

        return ExitCodes.Success;
    }

    public class Arguments : InstanceArgumentsBase { }
}
