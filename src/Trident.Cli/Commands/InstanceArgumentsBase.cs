using Spectre.Console.Cli;

namespace Trident.Cli.Commands;

public abstract class InstanceArgumentsBase : CommandSettings
{
    [CommandOption("-P|--profile <PATH>")]
    public string? Profile { get; set; }

    [CommandOption("-I|--instance <KEY>")]
    public string? Instance { get; set; }
}
