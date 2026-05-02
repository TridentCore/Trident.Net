using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Cli.Commands;
using TridentCore.Cli.Services;
using TridentCore.Cli.Utilities;

namespace TridentCore.Cli.Commands.Package;

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
        return PagingValidation.Validate(Sort, Index, Limit);
    }
}

public static class PagingValidation
{
    public static ValidationResult Validate(string sort, int index, int limit)
    {
        if (
            !string.Equals(sort, "asc", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(sort, "desc", StringComparison.OrdinalIgnoreCase)
        )
        {
            return ValidationResult.Error("--sort must be asc or desc.");
        }

        if (index < 0)
        {
            return ValidationResult.Error("--index must be greater than or equal to 0.");
        }

        return limit <= 0
            ? ValidationResult.Error("--limit must be greater than 0.")
            : ValidationResult.Success();
    }
}
