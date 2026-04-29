using Spectre.Console;
using Spectre.Console.Cli;
using Trident.Abstractions.Utilities;
using Trident.Cli.Services;
using Trident.Core.Models.PrismLauncherApi;
using Trident.Core.Services;

namespace Trident.Cli.Commands.Loader;

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
            throw new CliException($"Loader '{settings.LoaderId}' is not supported.", ExitCodes.Usage);
        }

        var uid = LoaderSupport.GetUid(settings.LoaderId);
        var versions = await prismLauncher
            .GetVersionsForMinecraftVersionAsync(uid, settings.Version, cancellationToken)
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

        var table = new Table().RoundedBorder();
        table.AddColumn("Version");
        table.AddColumn("LURL");
        table.AddColumn("Type");
        table.AddColumn("Recommended");
        table.AddColumn("Released");
        foreach (var item in result)
        {
            table.AddRow(
                item.Version,
                item.Lurl,
                item.Type,
                item.Recommended.ToString(),
                item.ReleaseTime.ToString("u")
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

        public override ValidationResult Validate()
        {
            if (!string.Equals(Sort, "asc", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Sort, "desc", StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.Error("--sort must be asc or desc.");
            }

            if (Index < 0)
            {
                return ValidationResult.Error("--index must be greater than or equal to 0.");
            }

            return Limit <= 0
                ? ValidationResult.Error("--limit must be greater than 0.")
                : ValidationResult.Success();
        }
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
