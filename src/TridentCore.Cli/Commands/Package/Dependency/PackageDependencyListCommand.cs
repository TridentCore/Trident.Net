using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
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
        var result = PackageOperation.DependencyList(
            repositories,
            resolver,
            settings.Purl,
            settings.GameVersion,
            settings.Loader,
            settings.ParsedKind,
            settings.Instance,
            settings.Profile
        ).GetAwaiter().GetResult();

        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
            return ExitCodes.Success;
        }

        if (result.Dependencies.Count == 0)
        {
            output.WriteEmptyState(
                "No dependencies",
                "This package version does not declare dependencies."
            );
            return ExitCodes.Success;
        }

        output.WriteTable(
            Utilities.PackageCliHelper.CreateDependencyTable($"Dependencies for {result.Purl}", result.Dependencies)
        );

        return ExitCodes.Success;
    }

    public class Arguments : PackageFilterSettings
    {
        [CommandArgument(0, "<PURL>")]
        public required string Purl { get; set; }
    }
}
