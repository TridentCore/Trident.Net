using Spectre.Console;
using Spectre.Console.Cli;
using Trident.Cli.Services;

namespace Trident.Cli.Commands.Package;

public class PackageListCommand(InstanceContextResolver resolver, CliOutput output)
    : InstanceCommandBase<PackageListCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var instance = ResolveInstance(settings);
        var packages = instance.Profile.Setup.Packages.Select(PackageDtos.FromEntry).ToArray();

        if (output.UseStructuredOutput)
        {
            output.WriteData(new { key = instance.Key, packages });
            return ExitCodes.Success;
        }

        if (packages.Length == 0)
        {
            output.WriteEmptyState("No packages", $"Instance {instance.Key} does not have installed packages.");
            return ExitCodes.Success;
        }

        var table = new Table().RoundedBorder();
        table.Title = new TableTitle($"[bold]Packages in {Markup.Escape(instance.Key)}[/]");
        table.AddColumn("PURL");
        table.AddColumn("Enabled");
        table.AddColumn("Source");
        table.AddColumn("Tags");
        foreach (var package in packages)
        {
            table.AddMarkupRow(
                Markup.Escape(package.Purl),
                CliOutput.FormatBoolean(package.Enabled, "enabled", "disabled"),
                CliOutput.FormatValue(package.Source),
                package.Tags.Count == 0 ? "[dim]-[/]" : Markup.Escape(string.Join(",", package.Tags))
            );
        }

        output.WriteTable(table);
        return ExitCodes.Success;
    }

    public class Arguments : InstanceArgumentsBase { }
}
