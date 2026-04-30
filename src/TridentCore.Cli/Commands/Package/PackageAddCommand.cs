using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Utilities;
using TridentCore.Cli.Services;
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
            throw new CliException("A package purl or stdin input is required.", ExitCodes.Usage);
        }

        var instance = ResolveInstance(settings);
        var guard = profileManager.GetMutable(instance.Key);
        var results = new List<AddResult>();
        var uniquePurls = purls.Distinct(StringComparer.Ordinal).ToArray();

        void ProcessPurl(string purl)
        {
            var parsed = PackageCliHelper.ParsePurl(purl);
            var normalized = PackageHelper.ToPurl(parsed.Label, parsed.Namespace, parsed.Pid, parsed.Vid);
            if (PackageCliHelper.ContainsProject(guard.Value, normalized))
            {
                results.Add(new(normalized, false, "already-installed"));
                return;
            }

            guard.Value.Setup.Packages.Add(
                new TridentCore.Abstractions.FileModels.Profile.Rice.Entry
                {
                    Enabled = true,
                    Purl = normalized,
                    Source = null,
                }
            );
            results.Add(new(normalized, true, null));
        }

        if (output.IsInteractive && !output.UseStructuredOutput && uniquePurls.Length > 1)
        {
            AnsiConsole
                .Progress()
                .AutoClear(false)
                .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn())
                .Start(progressContext =>
                {
                    var task = progressContext.AddTask("[blue]Processing packages[/]", maxValue: uniquePurls.Length);
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

        guard.DisposeAsync().AsTask().GetAwaiter().GetResult();

        var result = new { action = "package.add", key = instance.Key, results };
        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
        }
        else
        {
            var table = new Table().RoundedBorder();
            table.Title = new TableTitle($"[bold]Package add results for {Markup.Escape(instance.Key)}[/]");
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
            output.WriteSuccess($"Processed {results.Count} package(s) for {instance.Key}.");
        }

        return results.Any(x => !x.Added) ? ExitCodes.Partial : ExitCodes.Success;
    }

    public class Arguments : InstanceArgumentsBase
    {
        [CommandArgument(0, "[PURL]")]
        public string? Purl { get; set; }
    }

    private sealed record AddResult(string Purl, bool Added, string? Reason);
}
