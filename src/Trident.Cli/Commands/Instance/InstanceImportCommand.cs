using Spectre.Console.Cli;
using Trident.Abstractions.Importers;
using Trident.Cli.Services;
using Trident.Core.Services;

namespace Trident.Cli.Commands.Instance;

public class InstanceImportCommand(
    ProfileManager profileManager,
    ImporterAgent importerAgent,
    CliOutput output
) : Command<InstanceImportCommand.Arguments>
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        ImportAsync(settings, cancellationToken).GetAwaiter().GetResult();
        return ExitCodes.Success;
    }

    private async Task ImportAsync(Arguments settings, CancellationToken cancellationToken)
    {
        var sourcePath = Path.GetFullPath(settings.Path);
        if (!File.Exists(sourcePath))
        {
            throw new CliException($"Pack file '{sourcePath}' was not found.", ExitCodes.NotFound);
        }

        await using var file = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read
        );
        var memory = new MemoryStream();
        await file.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        memory.Position = 0;

        using var pack = new CompressedProfilePack(memory);
        var container = await importerAgent.ImportAsync(pack).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(settings.Name))
        {
            container.Profile.Name = settings.Name;
        }

        var identity = settings.Identity ?? container.Profile.Name ?? Path.GetFileNameWithoutExtension(sourcePath);
        var key = profileManager.RequestKey(identity);
        await importerAgent.ExtractFilesAsync(key.Key, container, pack).ConfigureAwait(false);
        profileManager.Add(key, container.Profile);

        var result = new
        {
            key = key.Key,
            name = container.Profile.Name,
            version = container.Profile.Setup.Version,
            loader = container.Profile.Setup.Loader,
            path = sourcePath,
        };

        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
        }
        else
        {
            output.WriteMessage($"Instance {key.Key} imported.");
        }
    }

    public class Arguments : CommandSettings
    {
        [CommandOption("--identity <KEY>")]
        public string? Identity { get; set; }

        [CommandOption("-n|--name <NAME>")]
        public string? Name { get; set; }

        [CommandArgument(0, "<PATH>")]
        public required string Path { get; set; }
    }
}
