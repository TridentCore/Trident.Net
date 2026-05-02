using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Abstractions;
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
        var instances = profileManager
            .Profiles.Select(x => new InstanceSummary(
                x.Item1,
                x.Item2.Name,
                x.Item2.Setup.Version,
                x.Item2.Setup.Loader,
                x.Item2.Setup.Source,
                x.Item2.Setup.Packages.Count,
                PathDef.Default.DirectoryOfHome(x.Item1)
            ))
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (output.UseStructuredOutput)
        {
            output.WriteData(instances);
            return ExitCodes.Success;
        }

        if (instances.Length == 0)
        {
            output.WriteEmptyState(
                "No instances",
                "Create one with: trident instance create --identity <key> --name <name> --version <version>"
            );
            return ExitCodes.Success;
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
        return ExitCodes.Success;
    }

    public class Arguments : CommandSettings { }

    private sealed record InstanceSummary(
        string Key,
        string Name,
        string Version,
        string? Loader,
        string? Source,
        int PackageCount,
        string Path
    );
}
