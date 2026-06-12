using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Instance;

public class InstanceListCommand(ProfileManager profileManager, CliOutput output)
    : Command<InstanceListCommand.Arguments>
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var instances = InstanceOperation.List(profileManager);

        if (output.UseStructuredOutput)
        {
            output.WriteData(instances);
            return ExitCodes.SUCCESS;
        }

        if (instances.Count == 0)
        {
            output.WriteEmptyState(
                "No instances",
                "Create one with: trident instance create --identity <key> --name <name> --version <version>"
            );
            return ExitCodes.SUCCESS;
        }

        var table = new Table().RoundedBorder();
        table.Title = new("[bold]Instances[/]");
        table.AddColumn("Key");
        table.AddColumn("Name");
        table.AddColumn("Version");
        table.AddColumn("Loader");
        table.AddColumn("Packages");
        table.AddColumn("Source");

        foreach (var instance in instances)
        {
            table.AddMarkupRow(
                $"[cyan]{Markup.Escape(instance.Key)}[/]",
                Markup.Escape(instance.Name),
                Markup.Escape(instance.Version),
                CliOutput.FormatValue(instance.Loader),
                Markup.Escape(instance.PackageCount.ToString()),
                CliOutput.FormatValue(instance.Source)
            );
        }

        output.WriteTable(table);
        return ExitCodes.SUCCESS;
    }

    public class Arguments : CommandSettings { }
}
