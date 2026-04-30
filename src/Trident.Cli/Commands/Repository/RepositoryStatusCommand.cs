using Spectre.Console;
using Spectre.Console.Cli;
using Trident.Cli.Services;
using Trident.Core.Services;

namespace Trident.Cli.Commands.Repository;

public class RepositoryStatusCommand(RepositoryAgent repositories, CliOutput output)
    : Command<RepositoryStatusCommand.Arguments>
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
        var labels = settings.Label is not null ? [settings.Label] : repositories.Labels.ToArray();
        var results = new List<RepositoryStatusDto>();

        async Task CheckAsync(Action? tick)
        {
            foreach (var label in labels)
            {
                var status = await repositories.CheckStatusAsync(label).ConfigureAwait(false);
                results.Add(new(label, status.SupportedLoaders, status.SupportedVersions.Count, status.SupportedKinds));
                tick?.Invoke();
            }
        }

        if (output.IsInteractive && !output.UseStructuredOutput && labels.Length > 1)
        {
            await AnsiConsole
                .Progress()
                .AutoClear(false)
                .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
                .StartAsync(async progressContext =>
                {
                    var task = progressContext.AddTask("[blue]Checking repositories[/]", maxValue: labels.Length);
                    await CheckAsync(() => task.Increment(1)).ConfigureAwait(false);
                })
                .ConfigureAwait(false);
        }
        else
        {
            await output
                .StatusAsync("Checking repository status...", async () => await CheckAsync(null).ConfigureAwait(false))
                .ConfigureAwait(false);
        }

        if (output.UseStructuredOutput)
        {
            output.WriteData(results);
            return;
        }

        if (results.Count == 0)
        {
            output.WriteEmptyState("No repositories", "No repository providers are configured.");
            return;
        }

        var table = new Table().RoundedBorder();
        table.Title = new TableTitle("[bold]Repository status[/]");
        table.AddColumn("Label");
        table.AddColumn("Loaders");
        table.AddColumn("Versions");
        table.AddColumn("Kinds");
        foreach (var result in results)
        {
            table.AddEscapedRow(
                result.Label,
                string.Join(",", result.SupportedLoaders),
                result.VersionCount.ToString(),
                string.Join(",", result.SupportedKinds)
            );
        }

        output.WriteTable(table);
    }

    private sealed record RepositoryStatusDto(
        string Label,
        IReadOnlyList<string> SupportedLoaders,
        int VersionCount,
        IReadOnlyList<Trident.Abstractions.Repositories.Resources.ResourceKind> SupportedKinds
    );

    public class Arguments : CommandSettings
    {
        [CommandOption("--label <LABEL>")]
        public string? Label { get; set; }
    }
}
