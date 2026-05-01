using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Package;

public class PackageListCommand(
    InstanceContextResolver resolver,
    RepositoryAgent repositories,
    CliOutput output
) : InstanceCommandBase<PackageListCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        ListAsync(settings, cancellationToken).GetAwaiter().GetResult();
        return ExitCodes.Success;
    }

    private async Task ListAsync(Arguments settings, CancellationToken cancellationToken)
    {
        var instance = ResolveInstance(settings);
        var entries = instance.Profile.Setup.Packages;

        if (entries.Count == 0)
        {
            if (output.UseStructuredOutput)
            {
                output.WriteData(
                    new { key = instance.Key, packages = Array.Empty<ResolvedLocalPackageDto>() }
                );
                return;
            }

            output.WriteEmptyState(
                "No packages",
                $"Instance {instance.Key} does not have installed packages."
            );
            return;
        }

        var resolved = await output
            .StatusAsync(
                "Resolving package metadata...",
                () => PackageDtos.ResolveEntriesAsync(entries, repositories, instance)
            )
            .ConfigureAwait(false);

        var paged = resolved.Skip(settings.Index).Take(settings.Limit).ToArray();

        if (output.UseStructuredOutput)
        {
            output.WriteData(
                new
                {
                    key = instance.Key,
                    packages = paged,
                    total = resolved.Count,
                }
            );
            return;
        }

        var table = new Table().RoundedBorder();
        table.Title = new TableTitle($"[bold]Packages in {Markup.Escape(instance.Key)}[/]");
        table.AddColumn("Name");
        table.AddColumn("Author");
        table.AddColumn("Kind");
        table.AddColumn("Enabled");
        table.AddColumn("PURL");
        foreach (var package in paged)
        {
            table.AddMarkupRow(
                CliOutput.FormatValue(package.ProjectName),
                CliOutput.FormatValue(package.Author),
                package.Kind?.ToString() is string k
                    ? CliOutput.FormatStatus(k, "blue")
                    : "[dim]-[/]",
                CliOutput.FormatBoolean(package.Enabled, "enabled", "disabled"),
                Markup.Escape(package.Purl)
            );
        }

        output.WriteTable(table);

        if (resolved.Count > paged.Length)
        {
            output.WriteInfo(
                $"Showing {paged.Length} of {resolved.Count} packages (offset {settings.Index}). Use --index and --limit to paginate."
            );
        }
    }

    public class Arguments : PagingSettings { }
}
