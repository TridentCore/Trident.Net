using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Cli.Services;
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
        return ExitCodes.Success;
    }

    private async Task InspectAsync(Arguments settings)
    {
        var parsed = PackageCliHelper.ParsePurl(settings.Purl);
        ResolvedInstanceContext? instance = null;
        LocalPackageDto? local = null;
        if (resolver.TryResolve(settings.Instance, settings.Profile, out var resolved))
        {
            instance = resolved;
            local = PackageDtos.FromEntry(
                PackageCliHelper.FindEntry(resolved.Profile, settings.Purl)
            );
        }

        var filter = PackageCliHelper.BuildFilter(
            settings.GameVersion,
            settings.Loader,
            settings.ParsedKind,
            instance
        );
        var package = await output
            .StatusAsync(
                "Resolving package metadata...",
                async () =>
                    await repositories
                        .ResolveAsync(
                            parsed.Label,
                            parsed.Namespace,
                            parsed.Pid,
                            parsed.Vid,
                            filter
                        )
                        .ConfigureAwait(false)
            )
            .ConfigureAwait(false);
        var packageDto = PackageDtos.FromPackage(package);

        if (output.UseStructuredOutput)
        {
            output.WriteData(
                new
                {
                    key = instance?.Key,
                    local,
                    package = packageDto,
                }
            );
            return;
        }

        output.WriteKeyValueTable(
            "Package details",
            ("PURL", packageDto.Purl),
            ("Project", packageDto.ProjectName),
            ("Version", packageDto.VersionName),
            ("Kind", packageDto.Kind.ToString()),
            ("Release", packageDto.ReleaseType.ToString()),
            ("Author", packageDto.Author),
            ("File", packageDto.FileName),
            ("Size", $"{packageDto.Size:n0} bytes"),
            ("Published", packageDto.PublishedAt.ToString("u")),
            ("Installed", local is null ? "no" : "yes")
        );

        if (!string.IsNullOrWhiteSpace(packageDto.Summary))
        {
            AnsiConsole.Write(
                new Panel(Markup.Escape(packageDto.Summary)).Header("Summary").RoundedBorder()
            );
        }

        if (packageDto.Dependencies.Count == 0)
        {
            output.WriteEmptyState(
                "No dependencies",
                "This package version does not declare dependencies."
            );
            return;
        }

        var table = new Table().RoundedBorder();
        table.Title = new TableTitle("[bold]Dependencies[/]");
        table.AddColumn("PURL");
        table.AddColumn("Required");
        foreach (var dependency in packageDto.Dependencies)
        {
            table.AddMarkupRow(
                Markup.Escape(dependency.Purl),
                CliOutput.FormatBoolean(dependency.IsRequired, "required", "optional")
            );
        }

        output.WriteTable(table);
    }

    public class Arguments : PackageFilterSettings
    {
        [CommandArgument(0, "<PURL>")]
        public required string Purl { get; set; }
    }
}
