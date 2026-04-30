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
        foreach (var label in labels)
        {
            var status = await repositories.CheckStatusAsync(label).ConfigureAwait(false);
            results.Add(new(label, status.SupportedLoaders, status.SupportedVersions.Count, status.SupportedKinds));
        }

        if (output.UseStructuredOutput)
        {
            output.WriteData(results);
            return;
        }

        var table = new Table().RoundedBorder();
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
