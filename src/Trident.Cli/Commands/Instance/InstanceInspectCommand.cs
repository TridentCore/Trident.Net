using Spectre.Console;
using Spectre.Console.Cli;
using Trident.Cli.Services;

namespace Trident.Cli.Commands.Instance;

public class InstanceInspectCommand(InstanceContextResolver resolver, CliOutput output)
    : InstanceCommandBase<InstanceInspectCommand.Arguments>(resolver)
{
    private const int PackagePreviewLimit = 5;

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
            instance.Profile.Setup.Packages.Take(PackagePreviewLimit).Select(x =>
                    new PackageSummary(x.Purl, x.Enabled, x.Source, x.Tags.ToArray())
                )
                .ToArray(),
            Math.Max(0, instance.Profile.Setup.Packages.Count - PackagePreviewLimit)
        );

        if (output.UseStructuredOutput)
        {
            output.WriteData(dto);
            return ExitCodes.Success;
        }

        output.WriteKeyValueTable(
            "Instance details",
            ("Key", dto.Key),
            ("Name", dto.Name),
            ("Version", dto.Version),
            ("Loader", dto.Loader),
            ("Source", dto.Source),
            ("Packages", dto.PackageCount.ToString()),
            ("Path", dto.Path),
            ("Profile", dto.ProfilePath)
        );

        if (dto.PackagePreview.Count == 0)
        {
            output.WriteEmptyState("No packages", "Add packages with: trident package add --instance <key> <purl>");
            return ExitCodes.Success;
        }

        var table = new Table().RoundedBorder();
        table.Title = new TableTitle("[bold]Package preview[/]");
        table.AddColumn("PURL");
        table.AddColumn("Enabled");
        table.AddColumn("Source");
        table.AddColumn("Tags");
        foreach (var package in dto.PackagePreview)
        {
            table.AddMarkupRow(
                Markup.Escape(package.Purl),
                CliOutput.FormatBoolean(package.Enabled, "enabled", "disabled"),
                CliOutput.FormatValue(package.Source),
                package.Tags.Count == 0 ? "[dim]-[/]" : Markup.Escape(string.Join(",", package.Tags))
            );
        }

        output.WriteTable(table);
        if (dto.HiddenPackageCount > 0)
        {
            output.WriteInfo(
                $"Showing {dto.PackagePreview.Count} of {dto.PackageCount} packages. Use 'trident package list --instance {dto.Key}' for the full list."
            );
        }

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
        IReadOnlyList<PackageSummary> PackagePreview,
        int HiddenPackageCount
    );

    private sealed record PackageSummary(
        string Purl,
        bool Enabled,
        string? Source,
        IReadOnlyList<string> Tags
    );
}
