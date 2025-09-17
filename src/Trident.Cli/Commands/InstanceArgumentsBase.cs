using Spectre.Console.Cli;

namespace Trident.Cli.Commands;

public abstract class InstanceArgumentsBase : CommandSettings
{
    [CommandOption("-P|--profile")]
    public string? Profile { get; set; }
    [CommandOption("-I|--instance")]
    public string? Instance { get; set; }
}
