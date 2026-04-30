using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Cli.Services;
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
        if (resolver.TryResolve(settings.Instance, settings.Profile, out var instance))
        {
            var entries = instance.Profile.Setup.Packages;
            var resolved = entries.Count > 0
                ? await output
                    .StatusAsync(
                        "Resolving package metadata...",
                        () => PackageDtos.ResolveEntriesAsync(entries, repositories, instance)
                    )
                    .ConfigureAwait(false)
                : [];

            var query = settings.Query;
            var local = resolved
                .Where(x =>
                    (x.ProjectName?.Contains(query, StringComparison.OrdinalIgnoreCase) is true)
                    || (x.Author?.Contains(query, StringComparison.OrdinalIgnoreCase) is true)
                    || (x.Summary?.Contains(query, StringComparison.OrdinalIgnoreCase) is true)
                    || x.Purl.Contains(query, StringComparison.OrdinalIgnoreCase)
                )
                .Where(x => settings.ParsedKind is null || x.Kind == settings.ParsedKind)
                .Where(x =>
                    settings.Repository is null
                    || x.Purl.StartsWith($"{settings.Repository}:", StringComparison.OrdinalIgnoreCase)
                )
                .Skip(settings.Index)
                .Take(settings.Limit)
                .ToArray();

            if (output.UseStructuredOutput)
            {
                output.WriteData(new { key = instance.Key, packages = local });
                return;
            }

            if (local.Length == 0)
            {
                output.WriteEmptyState("No local packages found", $"No package in {instance.Key} matched '{settings.Query}'.");
                return;
            }

            var localTable = new Table().RoundedBorder();
            localTable.Title = new TableTitle($"[bold]Packages in {Markup.Escape(instance.Key)}[/]");
            localTable.AddColumn("Name");
            localTable.AddColumn("Author");
            localTable.AddColumn("Kind");
            localTable.AddColumn("Enabled");
            localTable.AddColumn("PURL");
            foreach (var package in local)
            {
                localTable.AddMarkupRow(
                    CliOutput.FormatValue(package.ProjectName),
                    CliOutput.FormatValue(package.Author),
                    package.Kind?.ToString() is string k ? CliOutput.FormatStatus(k, "blue") : "[dim]-[/]",
                    CliOutput.FormatBoolean(package.Enabled, "enabled", "disabled"),
                    Markup.Escape(package.Purl)
                );
            }

            output.WriteTable(localTable);
            return;
        }

        var labels = settings.Repository is not null ? [settings.Repository] : repositories.Labels.ToArray();
        var filter = PackageCliHelper.BuildFilter(settings.GameVersion, settings.Loader, settings.ParsedKind);
        var items = new List<ExhibitDto>();
        foreach (var label in labels)
        {
            var handle = await output
                .StatusAsync(
                    $"Searching {label}...",
                    async () => await repositories.SearchAsync(label, settings.Query, filter).ConfigureAwait(false)
                )
                .ConfigureAwait(false);
            await output
                .StatusAsync(
                    $"Fetching results from {label}...",
                    async () =>
                    {
                        await foreach (var item in PaginationHelper.FetchWindowAsync(handle, settings.Index, settings.Limit, cancellationToken))
                        {
                            items.Add(PackageDtos.FromExhibit(item));
                        }
                    }
                )
                .ConfigureAwait(false);
        }

        if (output.UseStructuredOutput)
        {
            output.WriteData(items);
            return;
        }

        if (items.Count == 0)
        {
            output.WriteEmptyState("No packages found", $"No remote package matched '{settings.Query}'.");
            return;
        }

        var table = new Table().RoundedBorder();
        table.Title = new TableTitle($"[bold]Search results for {Markup.Escape(settings.Query)}[/]");
        table.AddColumn("Name");
        table.AddColumn("Author");
        table.AddColumn("Kind");
        table.AddColumn("Downloads");
        table.AddColumn("PURL");
        foreach (var item in items)
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
