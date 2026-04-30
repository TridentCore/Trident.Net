using Spectre.Console.Cli;

namespace TridentCore.Cli.Commands;

public abstract class InstanceArgumentsBase : CommandSettings
{
    [CommandOption("--profile <PATH>")]
    public string? Profile { get; set; }

    [CommandOption("-I|--instance <KEY>")]
    public string? Instance { get; set; }
}
