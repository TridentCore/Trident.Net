using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Cli.Utilities;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Package;

public class PackageInspectCommand(
    InstanceContextResolver resolver,
    RepositoryAgent repositories,
    CliOutput output
) : Command<PackageInspectCommand.Arguments>
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        InspectAsync(settings).GetAwaiter().GetResult();
        return ExitCodes.SUCCESS;
    }

    private async Task InspectAsync(Arguments settings)
    {
        var result = await PackageOperation
            .Inspect(resolver, repositories, settings.Pref,
                settings.GameVersion, settings.Loader, settings.Kind,
                settings.Instance, settings.Profile)
            .ConfigureAwait(false);

        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
            return;
        }

        output.WriteKeyValueTable(
            "Package details",
            ("PREF", result.Package.Pref),
            ("Project", result.Package.ProjectName),
            ("Version", result.Package.VersionName),
            ("Kind", result.Package.Kind.ToString()),
            ("Release", result.Package.ReleaseType.ToString()),
            ("Author", result.Package.Author),
            ("File", result.Package.FileName),
            ("Size", $"{result.Package.Size:n0} bytes"),
            ("Published", result.Package.PublishedAt.ToString("u")),
            ("Installed", result.Local is null ? "no" : "yes")
        );

        if (!string.IsNullOrWhiteSpace(result.Package.Summary))
        {
            AnsiConsole.Write(
                new Panel(Markup.Escape(result.Package.Summary)).Header("Summary").RoundedBorder()
            );
        }

        if (result.Package.Dependencies.Count == 0)
        {
            output.WriteEmptyState(
                "No dependencies",
                "This package version does not declare dependencies."
            );
            return;
        }

        output.WriteTable(
            PackageCliHelper.CreateDependencyTable("Dependencies", result.Package.Dependencies)
        );
    }

    public class Arguments : PackageFilterSettings
    {
        [CommandArgument(0, "<PREF>")]
        public required string Pref { get; set; }
    }
}
