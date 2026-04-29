using Spectre.Console;
using Spectre.Console.Cli;
using Trident.Cli.Services;

namespace Trident.Cli.Commands.Instance;

public class InstanceInspectCommand(InstanceContextResolver resolver, CliOutput output)
    : InstanceCommandBase<InstanceInspectCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var instance = ResolveInstance(settings);
        var dto = new InstanceDetail(
            instance.Key,
            instance.Profile.Name,
            instance.Profile.Setup.Version,
            instance.Profile.Setup.Loader,
            instance.Profile.Setup.Source,
            instance.InstancePath,
            instance.ProfilePath,
            instance.Profile.Setup.Packages.Count,
            instance.Profile.Setup.Packages.Select(x =>
                    new PackageSummary(x.Purl, x.Enabled, x.Source, x.Tags.ToArray())
                )
                .ToArray()
        );

        if (output.UseStructuredOutput)
        {
            output.WriteData(dto);
            return ExitCodes.Success;
        }

        var table = new Table().RoundedBorder().HideHeaders();
        table.AddColumn("Field");
        table.AddColumn("Value");
        table.AddRow("Key", dto.Key);
        table.AddRow("Name", dto.Name);
        table.AddRow("Version", dto.Version);
        table.AddRow("Loader", dto.Loader ?? "-");
        table.AddRow("Source", dto.Source ?? "-");
        table.AddRow("Packages", dto.PackageCount.ToString());
        table.AddRow("Path", dto.Path);
        table.AddRow("Profile", dto.ProfilePath);
        output.WriteTable(table);
        return ExitCodes.Success;
    }

    public class Arguments : InstanceArgumentsBase { }

    private sealed record InstanceDetail(
        string Key,
        string Name,
        string Version,
        string? Loader,
        string? Source,
        string Path,
        string ProfilePath,
        int PackageCount,
        IReadOnlyList<PackageSummary> Packages
    );

    private sealed record PackageSummary(
        string Purl,
        bool Enabled,
        string? Source,
        IReadOnlyList<string> Tags
    );
}
