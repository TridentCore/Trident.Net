using Spectre.Console.Cli;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Cli.Commands.Package;
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
    private const int PackagePreviewLimit = 5;

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
        var instance = ResolveInstance(settings);
        var entries = instance.Profile.Setup.Packages;
        var previewEntries = entries.Take(PackagePreviewLimit).ToList();

        var resolved =
            previewEntries.Count > 0
                ? await output
                    .StatusAsync(
                        "Resolving package metadata...",
                        () =>
                            PackageDtos.ResolveEntriesAsync(previewEntries, repositories, instance)
                    )
                    .ConfigureAwait(false)
                : [];

        var dto = new InstanceDetail(
            instance.Key,
            instance.Profile.Name,
            instance.Profile.Setup.Version,
            instance.Profile.Setup.Loader,
            instance.Profile.Setup.Source,
            instance.InstancePath,
            instance.ProfilePath,
            entries.Count,
            resolved
                .Select(p => new PackageSummary(
                    p.Purl,
                    p.Enabled,
                    p.Source,
                    p.Tags,
                    p.ProjectName,
                    p.Author,
                    p.Kind
                ))
                .ToArray(),
            Math.Max(0, entries.Count - PackagePreviewLimit)
        );

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
            ("Path", dto.Path),
            ("Profile", dto.ProfilePath)
        );

        if (dto.PackagePreview.Count == 0)
        {
            output.WriteEmptyState(
                "No packages",
                "Add packages with: trident package add --instance <key> <purl>"
            );
            return;
        }

        output.WriteTable(
            PackageCliHelper.CreatePackageTable("Package preview", dto.PackagePreview)
        );
        if (dto.HiddenPackageCount > 0)
        {
            output.WriteInfo(
                $"Showing {dto.PackagePreview.Count} of {dto.PackageCount} packages. Use 'trident package list --instance {dto.Key}' for the full list."
            );
        }
    }

    public class Arguments : InstanceArgumentsBase { }

    private sealed record InstanceDetail(
        string Key,
        string Name,
        string Version,
        string? Loader,
        string? Source,
        string Path,
        string ProfilePath,
        int PackageCount,
        IReadOnlyList<PackageSummary> PackagePreview,
        int HiddenPackageCount
    );

    private sealed record PackageSummary(
        string Purl,
        bool Enabled,
        string? Source,
        IReadOnlyList<string> Tags,
        string? ProjectName,
        string? Author,
        ResourceKind? Kind
    ) : IPackageTableRow;
}
