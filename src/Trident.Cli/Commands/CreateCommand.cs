using Spectre.Console;
using Spectre.Console.Cli;
using Trident.Cli.Services;
using Trident.Core.Services;
using Profile = Trident.Abstractions.FileModels.Profile;

namespace Trident.Cli.Commands;

public class CreateCommand(ProfileManager profileManager, CliOutput output)
    : CreationCommandBase<CreateCommand.Arguments>
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var key = profileManager.RequestKey(
            InstanceIdentityValidator.EnsureValid(settings.EffectiveIdentity)
        );
        var profile = new Profile()
        {
            Name = settings.Name,
            Setup = new()
            {
                Version = settings.Version,
                Source = null,
                Loader = settings.Loader,
            },
        };
        profileManager.Add(key, profile);
        var result = new
        {
            key = key.Key,
            profile.Name,
            version = profile.Setup.Version,
            loader = profile.Setup.Loader,
        };

        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
        }
        else
        {
            AnsiConsole.MarkupLine($"Instance [green]{key.Key}[/] created");
        }

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
