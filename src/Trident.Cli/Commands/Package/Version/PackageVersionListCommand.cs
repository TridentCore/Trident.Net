using Spectre.Console;
using Spectre.Console.Cli;
using Trident.Abstractions.Repositories;
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
        var handle = await repositories
            .InspectAsync(parsed.Label, parsed.Namespace, parsed.Pid, filter)
            .ConfigureAwait(false);
        var versions = new List<VersionDto>();
        await foreach (var version in FetchWindowAsync(handle, settings.Index, settings.Limit, cancellationToken))
        {
            versions.Add(PackageDtos.FromVersion(version));
        }

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

        var table = new Table().RoundedBorder();
        table.AddColumn("PURL");
        table.AddColumn("Name");
        table.AddColumn("Release");
        table.AddColumn("Downloads");
        foreach (var version in versions)
        {
            table.AddRow(
                version.Purl,
                version.VersionName,
                version.ReleaseType.ToString(),
                version.DownloadCount.ToString()
            );
        }

        output.WriteTable(table);
    }

    private static async IAsyncEnumerable<T> FetchWindowAsync<T>(
        IPaginationHandle<T> handle,
        int index,
        int limit,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var skipped = 0;
        var yielded = 0;
        var page = 0u;
        while (yielded < limit && skipped < index + limit && skipped < (int)handle.TotalCount)
        {
            handle.PageIndex = page++;
            var batch = (await handle.FetchAsync(cancellationToken).ConfigureAwait(false)).ToArray();
            if (batch.Length == 0)
            {
                yield break;
            }

            foreach (var item in batch)
            {
                if (skipped++ < index)
                {
                    continue;
                }

                yield return item;
                if (++yielded >= limit)
                {
                    yield break;
                }
            }
        }
    }

    public class Arguments : PagingSettings
    {
        [CommandArgument(0, "<PROJECT_PURL>")]
        public required string Purl { get; set; }
    }
}
