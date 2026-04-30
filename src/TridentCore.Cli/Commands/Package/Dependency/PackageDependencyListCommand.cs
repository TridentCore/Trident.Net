using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Package.Dependency;

public class PackageDependencyListCommand(
    InstanceContextResolver resolver,
    RepositoryAgent repositories,
    CliOutput output
) : Command<PackageDependencyListCommand.Arguments>
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
        var parsed = PackageCliHelper.ParsePurl(settings.Purl);
        resolver.TryResolve(settings.Instance, settings.Profile, out var instance);
        var filter = PackageCliHelper.BuildFilter(settings.GameVersion, settings.Loader, settings.ParsedKind, instance);
        var package = await output
            .StatusAsync(
                "Resolving package dependencies...",
                async () => await repositories
                    .ResolveAsync(parsed.Label, parsed.Namespace, parsed.Pid, parsed.Vid, filter)
                    .ConfigureAwait(false)
            )
            .ConfigureAwait(false);
        var dependencies = package.Dependencies.Select(PackageDtos.FromDependency).ToArray();

        if (output.UseStructuredOutput)
        {
            output.WriteData(new { purl = package.ToString(), dependencies });
            return;
        }

        if (dependencies.Length == 0)
        {
            output.WriteEmptyState("No dependencies", "This package version does not declare dependencies.");
            return;
        }

        var table = new Table().RoundedBorder();
        table.Title = new TableTitle($"[bold]Dependencies for {Markup.Escape(package.ToString())}[/]");
        table.AddColumn("PURL");
        table.AddColumn("Required");
        foreach (var dependency in dependencies)
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
