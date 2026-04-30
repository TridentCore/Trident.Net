using Spectre.Console;
using Spectre.Console.Cli;
using Trident.Cli.Services;

namespace Trident.Cli.Commands;

public class CreationArgumentsBase : CommandSettings
{
    [CommandOption("-n|--name <NAME>", true)]
    public required string Name { get; set; }

    [CommandOption("--identity <KEY>")]
    public string? Identity { get; set; }

    [CommandOption("-i|--id <KEY>")]
    public string? Id { get; set; }

    public string EffectiveIdentity => Identity ?? Id ?? string.Empty;

    public override ValidationResult Validate() =>
        InstanceIdentityValidator.TryValidate(EffectiveIdentity, out var error)
            ? ValidationResult.Success()
            : ValidationResult.Error(error);
}
