using Spectre.Console.Cli;
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
        return ExitCodes.Success;
    }

    private async Task ListAsync(Arguments settings, CancellationToken cancellationToken)
    {
        var instance = ResolveInstance(settings);
        var entries = instance.Profile.Setup.Packages;

        if (entries.Count == 0)
        {
            if (output.UseStructuredOutput)
            {
                output.WriteData(
                    new { key = instance.Key, packages = Array.Empty<ResolvedLocalPackageDto>() }
                );
                return;
            }

            output.WriteEmptyState(
                "No packages",
                $"Instance {instance.Key} does not have installed packages."
            );
            return;
        }

        var resolved = await output
            .StatusAsync(
                "Resolving package metadata...",
                () => PackageDtos.ResolveEntriesAsync(entries, repositories, instance)
            )
            .ConfigureAwait(false);

        var paged = resolved.Skip(settings.Index).Take(settings.Limit).ToArray();

        if (output.UseStructuredOutput)
        {
            output.WriteData(
                new
                {
                    key = instance.Key,
                    packages = paged,
                    total = resolved.Count,
                }
            );
            return;
        }

        output.WriteTable(
            PackageCliHelper.CreatePackageTable($"Packages in {instance.Key}", paged)
        );

        if (resolved.Count > paged.Length)
        {
            output.WriteInfo(
                $"Showing {paged.Length} of {resolved.Count} packages (offset {settings.Index}). Use --index and --limit to paginate."
            );
        }
    }

    public class Arguments : PagingSettings { }
}
