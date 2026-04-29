using Spectre.Console.Cli;
using Trident.Abstractions.FileModels;
using Trident.Abstractions.Utilities;
using Trident.Cli.Services;
using Trident.Core.Services;

namespace Trident.Cli.Commands.Package;

public class PackageAddCommand(
    InstanceContextResolver resolver,
    ProfileManager profileManager,
    StdinValueReader stdin,
    CliOutput output
) : InstanceCommandBase<PackageAddCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var purls = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.Purl))
        {
            purls.Add(settings.Purl);
        }

        purls.AddRange(stdin.ReadValuesIfRedirected());
        if (purls.Count == 0)
        {
            throw new CliException("A package purl or stdin input is required.", ExitCodes.Usage);
        }

        var instance = ResolveInstance(settings);
        var guard = profileManager.GetMutable(instance.Key);
        var results = new List<AddResult>();

        foreach (var purl in purls.Distinct(StringComparer.Ordinal))
        {
            var parsed = PackageCliHelper.ParsePurl(purl);
            var normalized = PackageHelper.ToPurl(parsed.Label, parsed.Namespace, parsed.Pid, parsed.Vid);
            if (PackageCliHelper.ContainsProject(guard.Value, normalized))
            {
                results.Add(new(normalized, false, "already-installed"));
                continue;
            }

            guard.Value.Setup.Packages.Add(
                new Profile.Rice.Entry
                {
                    Enabled = true,
                    Purl = normalized,
                    Source = null,
                }
            );
            results.Add(new(normalized, true, null));
        }

        guard.DisposeAsync().AsTask().GetAwaiter().GetResult();

        var result = new { action = "package.add", key = instance.Key, results };
        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
        }
        else
        {
            output.WriteMessage($"Processed {results.Count} package(s) for {instance.Key}.");
        }

        return results.Any(x => !x.Added) ? ExitCodes.Partial : ExitCodes.Success;
    }

    public class Arguments : InstanceArgumentsBase
    {
        [CommandArgument(0, "[PURL]")]
        public string? Purl { get; set; }
    }

    private sealed record AddResult(string Purl, bool Added, string? Reason);
}
