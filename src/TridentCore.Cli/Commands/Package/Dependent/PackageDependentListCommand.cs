using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Package.Dependent;

public class PackageDependentListCommand(
    InstanceContextResolver resolver,
    RepositoryAgent repositories,
    CliOutput output
) : InstanceCommandBase<PackageDependentListCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var instance = ResolveInstance(settings);
        var result = PackageOperation.DependentList(
            Resolver,
            repositories,
            settings.Purl,
            settings.GameVersion,
            settings.Loader,
            settings.ParsedKind,
            instance.Key,
            settings.Profile
        ).GetAwaiter().GetResult();

        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
            return ExitCodes.SUCCESS;
        }

        if (result.Failed.Count > 0)
        {
            output.WriteWarning($"Failed to inspect {result.Failed.Count} package(s).");
        }

        if (result.Dependents.Count == 0)
        {
            output.WriteEmptyState(
                "No dependents found",
                $"No enabled package in {instance.Key} depends on {settings.Purl}."
            );
            return ExitCodes.SUCCESS;
        }

        var table = new Table().RoundedBorder();
        table.Title = new($"[bold]Dependents for {Markup.Escape(settings.Purl)}[/]");
        table.AddColumn("PURL");
        table.AddColumn("Project");
        table.AddColumn("Version");
        foreach (var dep in result.Dependents)
        {
            table.AddMarkupRow(
                Markup.Escape(dep.Purl),
                Markup.Escape(dep.ProjectName ?? "-"),
                Markup.Escape(dep.VersionName ?? "-")
            );
        }

        output.WriteTable(table);

        return ExitCodes.SUCCESS;
    }

    public class Arguments : PackageFilterSettings
    {
        [CommandArgument(0, "<PURL>")]
        public required string Purl { get; set; }
    }
}
