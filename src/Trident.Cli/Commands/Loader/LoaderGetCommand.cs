using Spectre.Console;
using Spectre.Console.Cli;
using Trident.Abstractions.Utilities;
using Trident.Cli.Services;

namespace Trident.Cli.Commands.Loader;

public class LoaderGetCommand(InstanceContextResolver resolver, CliOutput output)
    : InstanceCommandBase<LoaderGetCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var instance = ResolveInstance(settings);
        var lurl = instance.Profile.Setup.Loader;
        var parsed = !string.IsNullOrWhiteSpace(lurl) && LoaderHelper.TryParse(lurl, out var result)
            ? new LoaderState(
                lurl,
                result.Identity,
                result.Version,
                LoaderHelper.ToDisplayName(result.Identity),
                LoaderSupport.IsSupported(result.Identity)
            )
            : new LoaderState(lurl, null, null, null, false);

        var dto = new { key = instance.Key, loader = parsed };
        if (output.UseStructuredOutput)
        {
            output.WriteData(dto);
            return ExitCodes.Success;
        }

        output.WriteKeyValueTable(
            "Instance loader",
            ("Instance", instance.Key),
            ("Loader", parsed.Lurl),
            ("Name", parsed.Name),
            ("Identity", parsed.Identity),
            ("Version", parsed.Version),
            ("Supported", parsed.Supported ? "yes" : "no")
        );
        if (parsed.Lurl is null)
        {
            output.WriteWarning("This instance does not have a loader configured.");
        }

        return ExitCodes.Success;
    }

    public class Arguments : InstanceArgumentsBase { }

    private sealed record LoaderState(
        string? Lurl,
        string? Identity,
        string? Version,
        string? Name,
        bool Supported
    );
}
