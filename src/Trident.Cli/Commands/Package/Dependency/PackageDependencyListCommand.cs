using Spectre.Console;
using Spectre.Console.Cli;
using Trident.Cli.Services;
using Trident.Core.Services;

namespace Trident.Cli.Commands.Package.Dependency;

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
        var package = await repositories
            .ResolveAsync(parsed.Label, parsed.Namespace, parsed.Pid, parsed.Vid, filter)
            .ConfigureAwait(false);
        var dependencies = package.Dependencies.Select(PackageDtos.FromDependency).ToArray();

        if (output.UseStructuredOutput)
        {
            output.WriteData(new { purl = package.ToString(), dependencies });
            return;
        }

        var table = new Table().RoundedBorder();
        table.AddColumn("PURL");
        table.AddColumn("Required");
        foreach (var dependency in dependencies)
        {
            table.AddEscapedRow(dependency.Purl, dependency.IsRequired.ToString());
        }

        output.WriteTable(table);
    }

    public class Arguments : PackageFilterSettings
    {
        [CommandArgument(0, "<PURL>")]
        public required string Purl { get; set; }
    }
}
