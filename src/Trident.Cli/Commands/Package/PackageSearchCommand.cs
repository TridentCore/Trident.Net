using Spectre.Console;
using Spectre.Console.Cli;
using Trident.Cli.Services;
using Trident.Core.Services;

namespace Trident.Cli.Commands.Package;

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
            var local = instance
                .Profile.Setup.Packages.Where(x =>
                    x.Purl.Contains(settings.Query, StringComparison.OrdinalIgnoreCase)
                    && (settings.Repository is null || x.Purl.StartsWith($"{settings.Repository}:", StringComparison.OrdinalIgnoreCase))
                )
                .Skip(settings.Index)
                .Take(settings.Limit)
                .Select(PackageDtos.FromEntry)
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
            localTable.AddColumn("PURL");
            localTable.AddColumn("Enabled");
            localTable.AddColumn("Source");
            localTable.AddColumn("Tags");
            foreach (var package in local)
            {
                localTable.AddMarkupRow(
                    Markup.Escape(package.Purl),
                    CliOutput.FormatBoolean(package.Enabled, "enabled", "disabled"),
                    CliOutput.FormatValue(package.Source),
                    package.Tags.Count == 0 ? "[dim]-[/]" : Markup.Escape(string.Join(",", package.Tags))
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
        table.AddColumn("PURL");
        table.AddColumn("Name");
        table.AddColumn("Kind");
        table.AddColumn("Downloads");
        foreach (var item in items)
        {
            table.AddEscapedRow(item.Purl, item.Name, item.Kind.ToString(), item.DownloadCount.ToString());
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
