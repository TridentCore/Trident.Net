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

            var localTable = new Table().RoundedBorder();
            localTable.AddColumn("PURL");
            localTable.AddColumn("Enabled");
            localTable.AddColumn("Source");
            localTable.AddColumn("Tags");
            foreach (var package in local)
            {
                localTable.AddRow(
                    package.Purl,
                    package.Enabled.ToString(),
                    package.Source ?? "-",
                    package.Tags.Count == 0 ? "-" : string.Join(",", package.Tags)
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
            var handle = await repositories.SearchAsync(label, settings.Query, filter).ConfigureAwait(false);
            await foreach (var item in PaginationHelper.FetchWindowAsync(handle, settings.Index, settings.Limit, cancellationToken))
            {
                items.Add(PackageDtos.FromExhibit(item));
            }
        }

        if (output.UseStructuredOutput)
        {
            output.WriteData(items);
            return;
        }

        var table = new Table().RoundedBorder();
        table.AddColumn("PURL");
        table.AddColumn("Name");
        table.AddColumn("Kind");
        table.AddColumn("Downloads");
        foreach (var item in items)
        {
            table.AddRow(item.Purl, item.Name, item.Kind.ToString(), item.DownloadCount.ToString());
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
