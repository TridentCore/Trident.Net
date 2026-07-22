using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Package.Version;

public class PackageVersionListCommand(RepositoryAgent repositories, CliOutput output)
    : Command<PackageVersionListCommand.Arguments>
{
    protected override int Execute(CommandContext context, Arguments settings, CancellationToken cancellationToken)
    {
        var result = PackageOperation
                    .VersionList(repositories,
                                 settings.Pref,
                                 settings.GameVersion,
                                 settings.Loader,
                                 settings.ParsedKind,
                                 settings.Sort,
                                 settings.Index,
                                 settings.Limit)
                    .GetAwaiter()
                    .GetResult();

        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
            return ExitCodes.SUCCESS;
        }

        if (result.Versions.Count == 0)
        {
            output.WriteEmptyState("No versions found", $"No versions matched filters for {settings.Pref}.");
            return ExitCodes.SUCCESS;
        }

        var table = new Table().RoundedBorder();
        table.Title = new($"[bold]Versions for {Markup.Escape(settings.Pref)}[/]");
        table.AddColumn("PREF");
        table.AddColumn("Name");
        table.AddColumn("Release");
        table.AddColumn("Downloads");
        foreach (var version in result.Versions)
        {
            var releaseColor = string.Equals(version.ReleaseType.ToString(),
                                             "Release",
                                             StringComparison.OrdinalIgnoreCase)
                                   ? "green"
                                   : "yellow";
            table.AddMarkupRow(Markup.Escape(version.Pref),
                               Markup.Escape(version.VersionName),
                               CliOutput.FormatStatus(version.ReleaseType.ToString(), releaseColor),
                               Markup.Escape(version.DownloadCount.ToString()));
        }

        output.WriteTable(table);

        return ExitCodes.SUCCESS;
    }

    public class Arguments : PagingSettings
    {
        [CommandArgument(0, "<PROJECT_PREF>")]
        public required string Pref { get; set; }
    }
}
