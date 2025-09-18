using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectre.Console;
using Trident.Core.Services;
using Profile = Trident.Abstractions.FileModels.Profile;

namespace Trident.Cli.Commands;

public class CreateCommand(ProfileManager profileManager) : CreationCommandBase<CreateCommand.Arguments>
{
    public override int Execute(CommandContext context, Arguments settings)
    {
        var key = profileManager.RequestKey(settings.Id);
        var profile = new Profile(settings.Name,
                                  new(null, settings.Version, settings.Loader, []),
                                  new Dictionary<string, object>());
        profileManager.Add(key, profile);
        AnsiConsole.WriteLine($"Instance {key.Key} created");

        return 0;
    }

    public class Arguments : CreationArgumentsBase
    {
        [CommandOption("-v|--version <VERSION>", isRequired: true)]
        public required string Version { get; set; }

        [CommandOption("-l|--loader <LURL>")]
        public string? Loader { get; set; }
    }
}
