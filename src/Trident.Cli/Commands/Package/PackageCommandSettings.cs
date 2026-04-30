using Spectre.Console;
using Spectre.Console.Cli;
using Trident.Abstractions.Repositories.Resources;
using Trident.Cli.Commands;
using Trident.Cli.Services;

namespace Trident.Cli.Commands.Package;

public abstract class PackageFilterSettings : InstanceArgumentsBase
{
    [CommandOption("--version <VERSION>")]
    public string? GameVersion { get; set; }

    [CommandOption("--loader <LOADER_ID>")]
    public string? Loader { get; set; }

    [CommandOption("--kind <KIND>")]
    public string? Kind { get; set; }

    public ResourceKind? ParsedKind => PackageCliHelper.ParseKind(Kind);
}

public abstract class PagingSettings : PackageFilterSettings
{
    [CommandOption("--sort <SORT>")]
    public string Sort { get; set; } = "desc";

    [CommandOption("--index <INDEX>")]
    public int Index { get; set; }

    [CommandOption("--limit <LIMIT>")]
    public int Limit { get; set; } = 20;

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
