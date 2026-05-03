using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Abstractions.Utilities;
using TridentCore.Cli.Commands.Package;
using TridentCore.Cli.Services;
using TridentCore.Core.Models.PrismLauncherApi;
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
        if (!LoaderSupport.IsSupported(settings.LoaderId))
        {
            throw new CliException(
                $"Loader '{settings.LoaderId}' is not supported.",
                ExitCodes.Usage
            );
        }

        var uid = LoaderSupport.GetUid(settings.LoaderId);
        var versions = await output
            .StatusAsync(
                "Loading loader versions...",
                async () =>
                    await prismLauncher
                        .GetVersionsForMinecraftVersionAsync(
                            uid,
                            settings.Version,
                            cancellationToken
                        )
                        .ConfigureAwait(false)
            )
            .ConfigureAwait(false);

        var sorted = string.Equals(settings.Sort, "asc", StringComparison.OrdinalIgnoreCase)
            ? versions.OrderBy(x => x.ReleaseTime)
            : versions.OrderByDescending(x => x.ReleaseTime);
        var page = sorted.Skip(settings.Index).Take(settings.Limit).ToArray();
        var result = page.Select(x => ToDto(settings.LoaderId, x)).ToArray();

        if (output.UseStructuredOutput)
        {
            output.WriteData(
                new
                {
                    loader = settings.LoaderId,
                    gameVersion = settings.Version,
                    total = versions.Count,
                    index = settings.Index,
                    limit = settings.Limit,
                    items = result,
                }
            );
            return;
        }

        if (result.Length == 0)
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
        foreach (var item in result)
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

    private static LoaderVersionDto ToDto(
        string loaderId,
        ComponentIndex.ComponentVersion version
    ) =>
        new(
            version.Version,
            LoaderHelper.ToLurl(loaderId, version.Version),
            version.Type,
            version.Recommended,
            version.ReleaseTime,
            version.Sha256
        );

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

    private sealed record LoaderVersionDto(
        string Version,
        string Lurl,
        string Type,
        bool Recommended,
        DateTimeOffset ReleaseTime,
        string Sha256
    );
}
