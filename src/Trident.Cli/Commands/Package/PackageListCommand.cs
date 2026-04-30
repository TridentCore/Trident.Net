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

        var table = new Table().RoundedBorder();
        table.AddColumn("PURL");
        table.AddColumn("Enabled");
        table.AddColumn("Source");
        table.AddColumn("Tags");
        foreach (var package in packages)
        {
            table.AddEscapedRow(
                package.Purl,
                package.Enabled.ToString(),
                package.Source ?? "-",
                package.Tags.Count == 0 ? "-" : string.Join(",", package.Tags)
            );
        }

        output.WriteTable(table);
        return ExitCodes.Success;
    }

    public class Arguments : InstanceArgumentsBase { }
}
