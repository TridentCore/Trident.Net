using Spectre.Console.Cli;

namespace Trident.Cli.Commands;

public class CreationArgumentsBase : CommandSettings
{
    [CommandOption("-n|--name <NAME>", true)]
    public required string Name { get; set; }

    [CommandOption("-i|--id <ID>", true)]
    public required string Id { get; set; }
}
