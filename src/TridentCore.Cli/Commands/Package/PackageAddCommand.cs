using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Cli.Utilities;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Package;

public class PackageAddCommand(
    InstanceContextResolver resolver,
    ProfileManager profileManager,
    StdinValueReader stdin,
    CliOutput output
) : InstanceCommandBase<PackageAddCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var purls = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.Purl))
        {
            purls.Add(settings.Purl);
        }

        purls.AddRange(stdin.ReadValuesIfRedirected());
        if (purls.Count == 0)
        {
            throw new CliException("A package purl or stdin input is required.", ExitCodes.USAGE);
        }

        var uniquePurls = purls.Distinct(StringComparer.Ordinal).ToArray();
        var results = new List<PackageAddResult>();

        void ProcessPurl(string purl)
        {
            var result = PackageOperation.Add(resolver, profileManager, purl, settings.Instance!, settings.Profile);
            results.Add(result);
        }

        if (output.IsInteractive && !output.UseStructuredOutput && uniquePurls.Length > 1)
        {
            AnsiConsole
                .Progress()
                .AutoClear(false)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn()
                )
                .Start(progressContext =>
                {
                    var task = progressContext.AddTask(
                        "[blue]Processing packages[/]",
                        maxValue: uniquePurls.Length
                    );
                    foreach (var purl in uniquePurls)
                    {
                        ProcessPurl(purl);
                        task.Increment(1);
                    }
                });
        }
        else
        {
            foreach (var purl in uniquePurls)
            {
                ProcessPurl(purl);
            }
        }

        if (output.UseStructuredOutput)
        {
            output.WriteData(new { results });
        }
        else
        {
            var table = new Table().RoundedBorder();
            table.Title = new($"[bold]Package add results[/]");
            table.AddColumn("PURL");
            table.AddColumn("Status");
            table.AddColumn("Reason");
            foreach (var item in results)
            {
                table.AddMarkupRow(
                    Markup.Escape(item.Purl),
                    item.Added ? "[green]added[/]" : "[yellow]skipped[/]",
                    CliOutput.FormatValue(item.Reason)
                );
            }

            output.WriteTable(table);
            output.WriteSuccess($"Processed {results.Count} package(s).");
        }

        return results.Any(x => !x.Added) ? ExitCodes.PARTIAL : ExitCodes.SUCCESS;
    }

    public class Arguments : InstanceArgumentsBase
    {
        [CommandArgument(0, "[PURL]")]
        public string? Purl { get; set; }
    }
}
