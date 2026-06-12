using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Cli.Utilities;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Package;

public class PackageListCommand(
    InstanceContextResolver resolver,
    RepositoryAgent repositories,
    CliOutput output
) : InstanceCommandBase<PackageListCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        ListAsync(settings, cancellationToken).GetAwaiter().GetResult();
        return ExitCodes.SUCCESS;
    }

    private async Task ListAsync(Arguments settings, CancellationToken cancellationToken)
    {
        var result = await PackageOperation
            .List(resolver, repositories, settings.Instance!, settings.Profile, settings.Index, settings.Limit)
            .ConfigureAwait(false);

        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
            return;
        }

        if (result.Packages.Count == 0)
        {
            output.WriteEmptyState(
                "No packages",
                $"Instance {result.Key} does not have installed packages."
            );
            return;
        }

        output.WriteTable(
            PackageCliHelper.CreatePackageTable($"Packages in {result.Key}", result.Packages)
        );

        if (result.Total > result.Packages.Count)
        {
            output.WriteInfo(
                $"Showing {result.Packages.Count} of {result.Total} packages (offset {settings.Index}). Use --index and --limit to paginate."
            );
        }
    }

    public class Arguments : PagingSettings { }
}
