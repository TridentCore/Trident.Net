using Spectre.Console.Cli;

namespace Trident.Cli.Commands;

public class ManageBranch : Command<ManageBranch.Settings>
{
    public class Settings : CommandSettings
    {
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        // This is a branch command, does not execute directly
        return 0;
    }
}
