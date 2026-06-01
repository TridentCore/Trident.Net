using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Cli.Commands.Package;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Loader;

public class LoaderVersionListCommand(PrismLauncherService prismLauncher, CliOutput output)
    : Command<LoaderVersionListCommand.Arguments>
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        ExecuteAsync(settings, cancellationToken).GetAwaiter().GetResult();
        return ExitCodes.Success;
    }

    private async Task ExecuteAsync(Arguments settings, CancellationToken cancellationToken)
    {
        var result = await LoaderOperation
            .VersionList(prismLauncher, settings.LoaderId, settings.Version, settings.Sort, settings.Index, settings.Limit)
            .ConfigureAwait(false);

        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
            return;
        }

        if (result.Items.Count == 0)
        {
            output.WriteEmptyState(
                "No loader versions found",
                $"No {settings.LoaderId} versions matched Minecraft {settings.Version}."
            );
            return;
        }

        var table = new Table().RoundedBorder();
        table.Title = new(
            $"[bold]{Markup.Escape(settings.LoaderId)} versions for Minecraft {Markup.Escape(settings.Version)}[/]"
        );
        table.AddColumn("Version");
        table.AddColumn("LURL");
        table.AddColumn("Type");
        table.AddColumn("Recommended");
        table.AddColumn("Released");
        foreach (var item in result.Items)
        {
            table.AddMarkupRow(
                Markup.Escape(item.Version),
                Markup.Escape(item.Lurl),
                Markup.Escape(item.Type),
                CliOutput.FormatBoolean(item.Recommended, "recommended", "no"),
                Markup.Escape(item.ReleaseTime.ToString("u"))
            );
        }

        output.WriteTable(table);
    }

    public class Arguments : CommandSettings
    {
        [CommandOption("-v|--version <MINECRAFT_VERSION>", true)]
        public required string Version { get; set; }

        [CommandOption("--sort <SORT>")]
        public string Sort { get; set; } = "desc";

        [CommandOption("--index <INDEX>")]
        public int Index { get; set; }

        [CommandOption("--limit <LIMIT>")]
        public int Limit { get; set; } = 20;

        [CommandArgument(0, "<LOADER_ID>")]
        public required string LoaderId { get; set; }

        public override ValidationResult Validate() =>
            PagingValidation.Validate(Sort, Index, Limit);
    }
}
