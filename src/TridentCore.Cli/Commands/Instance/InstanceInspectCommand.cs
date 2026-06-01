using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Cli.Utilities;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Instance;

public class InstanceInspectCommand(
    InstanceContextResolver resolver,
    RepositoryAgent repositories,
    CliOutput output
) : InstanceCommandBase<InstanceInspectCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        InspectAsync(settings, cancellationToken).GetAwaiter().GetResult();
        return ExitCodes.Success;
    }

    private async Task InspectAsync(Arguments settings, CancellationToken cancellationToken)
    {
        var dto = await InstanceOperation
            .Inspect(resolver, repositories, settings.Instance!, settings.Profile)
            .ConfigureAwait(false);

        if (output.UseStructuredOutput)
        {
            output.WriteData(dto);
            return;
        }

        output.WriteKeyValueTable(
            "Instance details",
            ("Key", dto.Key),
            ("Name", dto.Name),
            ("Version", dto.Version),
            ("Loader", dto.Loader),
            ("Source", dto.Source),
            ("Packages", dto.PackageCount.ToString()),
            ("Path", dto.InstancePath),
            ("Profile", dto.ProfilePath)
        );

        if (dto.Packages.Count == 0)
        {
            output.WriteEmptyState(
                "No packages",
                "Add packages with: trident package add --instance <key> <purl>"
            );
            return;
        }

        output.WriteTable(
            PackageCliHelper.CreatePackageTable("Package preview", dto.Packages)
        );
        if (dto.HiddenPackageCount > 0)
        {
            output.WriteInfo(
                $"Showing {dto.Packages.Count} of {dto.PackageCount} packages. Use 'trident package list --instance {dto.Key}' for the full list."
            );
        }
    }

    public class Arguments : InstanceArgumentsBase { }
}
