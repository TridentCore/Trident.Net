using Spectre.Console.Cli;
using Trident.Abstractions;
using Trident.Cli.Services;
using Trident.Core.Services;

namespace Trident.Cli.Commands.Instance;

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

        var bomb = PathDef.Default.FileOfBomb(instance.Key);
        var dir = Path.GetDirectoryName(bomb);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(bomb, "delete requested by trident cli");
        profileManager.Remove(instance.Key);

        var result = new
        {
            action = "delete",
            key = instance.Key,
            bomb,
            deletedImmediately = false,
        };

        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
        }
        else
        {
            output.WriteMessage($"Instance {instance.Key} marked for deletion.");
        }

        return ExitCodes.Success;
    }

    public class Arguments : InstanceArgumentsBase
    {
        [CommandOption("-y|--yes")]
        public bool Yes { get; set; }
    }
}
