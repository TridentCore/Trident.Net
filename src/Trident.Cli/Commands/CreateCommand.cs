using Spectre.Console;
using Spectre.Console.Cli;
using Trident.Core.Services;
using Profile = Trident.Abstractions.FileModels.Profile;

namespace Trident.Cli.Commands;

public class CreateCommand(ProfileManager profileManager) : CreationCommandBase<CreateCommand.Arguments>
{
    public override int Execute(CommandContext context, Arguments settings, CancellationToken cancellationToken)
    {
        var key = profileManager.RequestKey(settings.Id);
        var profile = new Profile(settings.Name,
                                  new(null, settings.Version, settings.Loader, [], []),
                                  new Dictionary<string, object>());
        profileManager.Add(key, profile);
        AnsiConsole.WriteLine($"Instance {key.Key} created");

        return 0;
    }

    #region Nested type: Arguments

    public class Arguments : CreationArgumentsBase
    {
        [CommandOption("-v|--version <VERSION>", true)]
        public required string Version { get; set; }

        [CommandOption("-l|--loader <LURL>")]
        public string? Loader { get; set; }
    }

    #endregion
}
