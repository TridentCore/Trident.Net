using Spectre.Console;
using Spectre.Console.Cli;
using Trident.Abstractions.Utilities;
using Trident.Cli.Services;
using Trident.Core.Services;

namespace Trident.Cli.Commands.Package.Dependent;

public class PackageDependentListCommand(
    InstanceContextResolver resolver,
    RepositoryAgent repositories,
    CliOutput output
) : InstanceCommandBase<PackageDependentListCommand.Arguments>(resolver)
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
        var instance = ResolveInstance(settings);
        var target = PackageCliHelper.ParsePurl(settings.Purl);
        var filter = PackageCliHelper.BuildFilter(settings.GameVersion, settings.Loader, settings.ParsedKind, instance);
        var dependents = new List<DependentDto>();
        var failed = new List<string>();

        foreach (var entry in instance.Profile.Setup.Packages)
        {
            if (!entry.Enabled || !PackageHelper.TryParse(entry.Purl, out var parsed))
            {
                continue;
            }

            try
            {
                var package = await repositories
                    .ResolveAsync(parsed.Label, parsed.Namespace, parsed.Pid, parsed.Vid, filter)
                    .ConfigureAwait(false);
                if (package.Dependencies.Any(x =>
                        x.Label == target.Label
                        && x.Namespace == target.Namespace
                        && x.ProjectId == target.Pid
                    ))
                {
                    dependents.Add(new(entry.Purl, package.ProjectName, package.VersionName));
                }
            }
            catch
            {
                failed.Add(entry.Purl);
            }
        }

        if (output.UseStructuredOutput)
        {
            output.WriteData(new { key = instance.Key, target = settings.Purl, scope = "instance", dependents, failed });
            return;
        }

        var table = new Table().RoundedBorder();
        table.AddColumn("PURL");
        table.AddColumn("Project");
        table.AddColumn("Version");
        foreach (var dependent in dependents)
        {
            table.AddRow(dependent.Purl, dependent.ProjectName, dependent.VersionName);
        }

        output.WriteTable(table);
    }

    private sealed record DependentDto(string Purl, string ProjectName, string VersionName);

    public class Arguments : PackageFilterSettings
    {
        [CommandArgument(0, "<PURL>")]
        public required string Purl { get; set; }
    }
}
