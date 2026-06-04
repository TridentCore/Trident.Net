using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Instance;

public class InstanceResetCommand(
    InstanceContextResolver resolver,
    InstanceManager instanceManager,
    CliOutput output
) : InstanceCommandBase<InstanceResetCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var instance = ResolveInstance(settings);
        output.RequireConfirmation(
            $"Reset build artifacts for instance '{instance.Key}'?",
            settings.Yes
        );

        var result = InstanceOperation.Reset(resolver, instanceManager, instance.Key, settings.Profile);

        if (output.UseStructuredOutput)
        {
            output.WriteData(new { action = "reset", key = result.Key, deleted = result.Deleted });
        }
        else
        {
            output.WriteKeyValueTable(
                "Instance reset",
                ("Instance", result.Key),
                ("Deleted Items", result.Deleted.Count.ToString())
            );
            if (result.Deleted.Count > 0)
            {
                var table = new Table().RoundedBorder();
                table.Title = new("[bold]Deleted paths[/]");
                table.AddColumn("Path");
                foreach (var path in result.Deleted)
                {
                    table.AddEscapedRow(path);
                }

                output.WriteTable(table);
            }

            output.WriteSuccess($"Instance {result.Key} reset.");
        }

        return ExitCodes.SUCCESS;
    }

    public class Arguments : InstanceArgumentsBase
    {
        [CommandOption("-y|--yes")]
        public bool Yes { get; set; }
    }
}
