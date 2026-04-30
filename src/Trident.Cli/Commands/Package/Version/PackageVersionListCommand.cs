using Spectre.Console;
using Spectre.Console.Cli;
using Trident.Cli.Services;
using Trident.Core.Services;

namespace Trident.Cli.Commands.Package.Version;

public class PackageVersionListCommand(RepositoryAgent repositories, CliOutput output)
    : Command<PackageVersionListCommand.Arguments>
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        ExecuteAsync(settings, cancellationToken).GetAwaiter().GetResult();
        return ExitCodes.Success;
    }

    private async Task ExecuteAsync(Arguments settings, CancellationToken cancellationToken)
    {
        var parsed = PackageCliHelper.ParsePurl(settings.Purl);
        var filter = PackageCliHelper.BuildFilter(settings.GameVersion, settings.Loader, settings.ParsedKind);
        var handle = await output
            .StatusAsync(
                "Loading package versions...",
                async () => await repositories
                    .InspectAsync(parsed.Label, parsed.Namespace, parsed.Pid, filter)
                    .ConfigureAwait(false)
            )
            .ConfigureAwait(false);
        var versions = new List<VersionDto>();
        await output
            .StatusAsync(
                "Fetching version page...",
                async () =>
                {
                    await foreach (var version in PaginationHelper.FetchWindowAsync(handle, settings.Index, settings.Limit, cancellationToken))
                    {
                        versions.Add(PackageDtos.FromVersion(version));
                    }
                }
            )
            .ConfigureAwait(false);

        if (string.Equals(settings.Sort, "asc", StringComparison.OrdinalIgnoreCase))
        {
            versions = [.. versions.OrderBy(x => x.PublishedAt)];
        }
        else
        {
            versions = [.. versions.OrderByDescending(x => x.PublishedAt)];
        }

        if (output.UseStructuredOutput)
        {
            output.WriteData(new { project = settings.Purl, total = handle.TotalCount, versions });
            return;
        }

        if (versions.Count == 0)
        {
            output.WriteEmptyState("No versions found", $"No versions matched filters for {settings.Purl}.");
            return;
        }

        var table = new Table().RoundedBorder();
        table.Title = new TableTitle($"[bold]Versions for {Markup.Escape(settings.Purl)}[/]");
        table.AddColumn("PURL");
        table.AddColumn("Name");
        table.AddColumn("Release");
        table.AddColumn("Downloads");
        foreach (var version in versions)
        {
            var releaseColor = string.Equals(version.ReleaseType.ToString(), "Release", StringComparison.OrdinalIgnoreCase)
                ? "green"
                : "yellow";
            table.AddMarkupRow(
                Markup.Escape(version.Purl),
                Markup.Escape(version.VersionName),
                CliOutput.FormatStatus(version.ReleaseType.ToString(), releaseColor),
                Markup.Escape(version.DownloadCount.ToString())
            );
        }

        output.WriteTable(table);
    }

    public class Arguments : PagingSettings
    {
        [CommandArgument(0, "<PROJECT_PURL>")]
        public required string Purl { get; set; }
    }
}
