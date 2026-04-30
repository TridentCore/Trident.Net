using Spectre.Console.Cli;
using Trident.Cli.Services;
using Trident.Core.Services;

namespace Trident.Cli.Commands.Package;

public class PackageEnableCommand(
    InstanceContextResolver resolver,
    ProfileManager profileManager,
    CliOutput output
) : InstanceCommandBase<PackageEnableCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    ) => SetEnabled(settings, true);

    protected int SetEnabled(Arguments settings, bool enabled)
    {
        var instance = ResolveInstance(settings);
        var guard = profileManager.GetMutable(instance.Key);
        var entry = PackageCliHelper.FindEntry(guard.Value, settings.Purl);
        entry.Enabled = enabled;
        guard.DisposeAsync().AsTask().GetAwaiter().GetResult();

        var result = new { action = enabled ? "package.enable" : "package.disable", key = instance.Key, entry.Purl, entry.Enabled };
        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
        }
        else
        {
            output.WriteKeyValueTable(
                enabled ? "Package enabled" : "Package disabled",
                ("Instance", instance.Key),
                ("PURL", entry.Purl),
                ("State", enabled ? "enabled" : "disabled")
            );
            output.WriteSuccess($"Package {entry.Purl} {(enabled ? "enabled" : "disabled")}.");
        }

        return ExitCodes.Success;
    }

    public class Arguments : InstanceArgumentsBase
    {
        [CommandArgument(0, "<PURL>")]
        public required string Purl { get; set; }
    }
}
