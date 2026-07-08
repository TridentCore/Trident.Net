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
        var prefs = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.Pref))
        {
            prefs.Add(settings.Pref);
        }

        prefs.AddRange(stdin.ReadValuesIfRedirected());
        if (prefs.Count == 0)
        {
            throw new CliException("A package pref or stdin input is required.", ExitCodes.USAGE);
        }

        var uniquePrefs = prefs.Distinct(StringComparer.Ordinal).ToArray();
        var results = new List<PackageAddResult>();

        void ProcessPref(string pref)
        {
            var result = PackageOperation.Add(Resolver, profileManager, pref, settings.Instance!, settings.Profile);
            results.Add(result);
        }

        if (output.IsInteractive && !output.UseStructuredOutput && uniquePrefs.Length > 1)
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
                        maxValue: uniquePrefs.Length
                    );
                    foreach (var pref in uniquePrefs)
                    {
                        ProcessPref(pref);
                        task.Increment(1);
                    }
                });
        }
        else
        {
            foreach (var pref in uniquePrefs)
            {
                ProcessPref(pref);
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
            table.AddColumn("PREF");
            table.AddColumn("Status");
            table.AddColumn("Reason");
            foreach (var item in results)
            {
                table.AddMarkupRow(
                    Markup.Escape(item.Pref),
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
        [CommandArgument(0, "[PREF]")]
        public string? Pref { get; set; }
    }
}
