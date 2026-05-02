using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Abstractions.Utilities;
using TridentCore.Cli.Services;
using TridentCore.Cli.Utilities;
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
        ExecuteAsync(settings).GetAwaiter().GetResult();
        return ExitCodes.Success;
    }

    private async Task ExecuteAsync(Arguments settings)
    {
        var instance = ResolveInstance(settings);
        var target = PackageCliHelper.ParsePurl(settings.Purl);
        var filter = PackageCliHelper.BuildFilter(
            settings.GameVersion,
            settings.Loader,
            settings.ParsedKind,
            instance
        );
        var dependents = new List<DependentDto>();
        var failed = new List<string>();
        var candidates = instance
            .Profile.Setup.Packages.Where(x => x.Enabled && PackageHelper.TryParse(x.Purl, out _))
            .ToArray();

        async Task ScanAsync(Action? tick)
        {
            foreach (var entry in candidates)
            {
                if (!PackageHelper.TryParse(entry.Purl, out var parsed))
                {
                    tick?.Invoke();
                    continue;
                }

                try
                {
                    var package = await repositories
                        .ResolveAsync(
                            parsed.Label,
                            parsed.Namespace,
                            parsed.Pid,
                            parsed.Vid,
                            filter
                        )
                        .ConfigureAwait(false);
                    if (
                        package.Dependencies.Any(x =>
                            x.Label == target.Label
                            && x.Namespace == target.Namespace
                            && x.ProjectId == target.Pid
                        )
                    )
                    {
                        dependents.Add(new(entry.Purl, package.ProjectName, package.VersionName));
                    }
                }
                catch
                {
                    failed.Add(entry.Purl);
                }

                tick?.Invoke();
            }
        }

        if (output.IsInteractive && !output.UseStructuredOutput && candidates.Length > 1)
        {
            await AnsiConsole
                .Progress()
                .AutoClear(false)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn()
                )
                .StartAsync(async progressContext =>
                {
                    var task = progressContext.AddTask(
                        "[blue]Scanning installed packages[/]",
                        maxValue: candidates.Length
                    );
                    await ScanAsync(() => task.Increment(1)).ConfigureAwait(false);
                })
                .ConfigureAwait(false);
        }
        else
        {
            await output
                .StatusAsync(
                    "Scanning installed packages...",
                    async () => await ScanAsync(null).ConfigureAwait(false)
                )
                .ConfigureAwait(false);
        }

        if (failed.Count > 0 && !output.UseStructuredOutput)
        {
            output.WriteWarning($"Failed to inspect {failed.Count} package(s).");
        }

        if (dependents.Count == 0 && !output.UseStructuredOutput)
        {
            output.WriteEmptyState(
                "No dependents found",
                $"No enabled package in {instance.Key} depends on {settings.Purl}."
            );
            if (failed.Count > 0)
            {
                output.WriteTable(CreateFailedInspectionTable(failed));
            }

            return;
        }

        if (output.UseStructuredOutput)
        {
            output.WriteData(
                new
                {
                    key = instance.Key,
                    target = settings.Purl,
                    scope = "instance",
                    dependents,
                    failed,
                }
            );
            return;
        }

        var table = new Table().RoundedBorder();
        table.Title = new($"[bold]Dependents of {Markup.Escape(settings.Purl)}[/]");
        table.AddColumn("PURL");
        table.AddColumn("Project");
        table.AddColumn("Version");
        foreach (var dependent in dependents)
        {
            table.AddEscapedRow(dependent.Purl, dependent.ProjectName, dependent.VersionName);
        }

        output.WriteTable(table);

        if (failed.Count > 0)
        {
            output.WriteTable(CreateFailedInspectionTable(failed));
        }
    }

    private static Table CreateFailedInspectionTable(IEnumerable<string> failed)
    {
        var failedTable = new Table().RoundedBorder();
        failedTable.Title = new("[yellow]Failed inspections[/]");
        failedTable.AddColumn("PURL");
        foreach (var purl in failed)
        {
            failedTable.AddEscapedRow(purl);
        }

        return failedTable;
    }

    private sealed record DependentDto(string Purl, string ProjectName, string VersionName);

    public class Arguments : PackageFilterSettings
    {
        [CommandArgument(0, "<PURL>")]
        public required string Purl { get; set; }
    }
}
