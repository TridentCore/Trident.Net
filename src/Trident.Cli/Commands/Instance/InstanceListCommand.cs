using Spectre.Console;
using Spectre.Console.Cli;
using Trident.Abstractions;
using Trident.Cli.Services;
using Trident.Core.Services;

namespace Trident.Cli.Commands.Instance;

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

        var table = new Table().RoundedBorder();
        table.AddColumn("Key");
        table.AddColumn("Name");
        table.AddColumn("Version");
        table.AddColumn("Loader");
        table.AddColumn("Packages");
        table.AddColumn("Source");

        foreach (var instance in instances)
        {
            table.AddRow(
                instance.Key,
                instance.Name,
                instance.Version,
                instance.Loader ?? "-",
                instance.PackageCount.ToString(),
                instance.Source ?? "-"
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
