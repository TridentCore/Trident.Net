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
            settings.Pref,
            settings.GameVersion,
            settings.Loader,
            settings.ParsedKind,
            settings.Instance,
            settings.Profile
        ).GetAwaiter().GetResult();

        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
            return ExitCodes.SUCCESS;
        }

        if (result.Dependencies.Count == 0)
        {
            output.WriteEmptyState(
                "No dependencies",
                "This package version does not declare dependencies."
            );
            return ExitCodes.SUCCESS;
        }

        output.WriteTable(
            Utilities.PackageCliHelper.CreateDependencyTable($"Dependencies for {result.Pref}", result.Dependencies)
        );

        return ExitCodes.SUCCESS;
    }

    public class Arguments : PackageFilterSettings
    {
        [CommandArgument(0, "<PREF>")]
        public required string Pref { get; set; }
    }
}
