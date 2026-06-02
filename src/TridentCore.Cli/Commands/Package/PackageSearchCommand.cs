using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Cli.Commands.Package;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Cli.Utilities;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Package;

public class PackageSearchCommand(
    InstanceContextResolver resolver,
    RepositoryAgent repositories,
    CliOutput output
) : Command<PackageSearchCommand.Arguments>
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        SearchAsync(settings, cancellationToken).GetAwaiter().GetResult();
        return ExitCodes.Success;
    }

    private async Task SearchAsync(Arguments settings, CancellationToken cancellationToken)
    {
        var kind = PackageCliHelper.ParseKind(settings.Kind);

        if (resolver.TryResolve(settings.Instance, settings.Profile, out var instance))
        {
            var local = await PackageOperation
                .SearchLocal(resolver, repositories, settings.Query, settings.Repository,
                    kind, settings.Instance, settings.Profile, settings.Index, settings.Limit)
                .ConfigureAwait(false);

            if (output.UseStructuredOutput)
            {
                output.WriteData(local);
                return;
            }

            if (local.Packages.Count == 0)
            {
                output.WriteEmptyState(
                    "No local packages found",
                    $"No package in {local.Key} matched '{settings.Query}'."
                );
                return;
            }

            output.WriteTable(
                PackageCliHelper.CreatePackageTable(
                    $"Packages in {local.Key} ({local.Total} total, showing {local.Packages.Count})",
                    local.Packages)
            );
            return;
        }

        if (settings.Repository is null)
        {
            throw new CliException(
                "--repository is required for remote search. Use -R <label> to specify a repository.",
                ExitCodes.Usage
            );
        }

        var result = await PackageOperation
            .SearchRemote(repositories, settings.Query, settings.Repository,
                settings.GameVersion, settings.Loader, kind, settings.Index, settings.Limit)
            .ConfigureAwait(false);

        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
            return;
        }

        if (result.Packages.Count == 0)
        {
            output.WriteEmptyState(
                "No packages found",
                $"No remote package matched '{settings.Query}' in {settings.Repository}."
            );
            return;
        }

        var table = new Table().RoundedBorder();
        table.Title = new(
            $"[bold]Search results for {Markup.Escape(settings.Query)}[/] in {settings.Repository} ({result.Total} total, showing {result.Packages.Count})"
        );
        table.AddColumn("Name");
        table.AddColumn("Author");
        table.AddColumn("Kind");
        table.AddColumn("Downloads");
        table.AddColumn("PURL");
        foreach (var item in result.Packages)
        {
            table.AddMarkupRow(
                CliOutput.FormatValue(item.Name),
                CliOutput.FormatValue(item.Author),
                CliOutput.FormatStatus(item.Kind.ToString(), "blue"),
                item.DownloadCount.ToString("n0"),
                Markup.Escape(item.Purl)
            );
        }

        output.WriteTable(table);
    }

    public class Arguments : PagingSettings
    {
        [CommandOption("-R|--repository <LABEL>")]
        public string? Repository { get; set; }

        [CommandArgument(0, "<QUERY>")]
        public required string Query { get; set; }
    }
}
