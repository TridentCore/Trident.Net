using Spectre.Console.Cli;
using Trident.Abstractions.Utilities;
using Trident.Cli.Services;
using Trident.Core.Services;

namespace Trident.Cli.Commands.Package.Version;

public class PackageVersionSetCommand(
    InstanceContextResolver resolver,
    ProfileManager profileManager,
    CliOutput output
) : InstanceCommandBase<PackageVersionSetCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var parsed = PackageCliHelper.ParsePurl(settings.Purl);
        if (string.IsNullOrWhiteSpace(parsed.Vid))
        {
            throw new CliException("A version purl with @version is required.", ExitCodes.Usage);
        }

        var instance = ResolveInstance(settings);
        var guard = profileManager.GetMutable(instance.Key);
        var entry = PackageCliHelper.FindEntry(guard.Value, settings.Purl);
        var oldPurl = entry.Purl;
        entry.Purl = PackageHelper.ToPurl(parsed.Label, parsed.Namespace, parsed.Pid, parsed.Vid);
        guard.DisposeAsync().AsTask().GetAwaiter().GetResult();

        var result = new { action = "package.version.set", key = instance.Key, oldPurl, entry.Purl };
        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
        }
        else
        {
            output.WriteMessage($"Package version updated to {entry.Purl}.");
        }

        return ExitCodes.Success;
    }

    public class Arguments : InstanceArgumentsBase
    {
        [CommandArgument(0, "<VERSION_PURL>")]
        public required string Purl { get; set; }
    }
}
