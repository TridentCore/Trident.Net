using Spectre.Console.Cli;
using TridentCore.Abstractions.Importers;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Instance;

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
        await output
            .StatusAsync(
                "Reading pack archive...",
                async () => await file.CopyToAsync(memory, cancellationToken).ConfigureAwait(false)
            )
            .ConfigureAwait(false);
        memory.Position = 0;

        using var pack = new CompressedProfilePack(memory);
        var container = await output
            .StatusAsync(
                "Inspecting pack metadata...",
                async () => await importerAgent.ImportAsync(pack).ConfigureAwait(false)
            )
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(settings.Name))
        {
            container.Profile.Name = settings.Name;
        }

        var identity = InstanceIdentityValidator.EnsureValid(
            settings.Identity ?? container.Profile.Name ?? Path.GetFileNameWithoutExtension(sourcePath)
        );
        var key = profileManager.RequestKey(identity);
        await output
            .StatusAsync(
                "Extracting instance files...",
                async () => await importerAgent.ExtractFilesAsync(key.Key, container, pack).ConfigureAwait(false)
            )
            .ConfigureAwait(false);
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
            output.WriteKeyValueTable(
                "Instance imported",
                ("Key", key.Key),
                ("Name", container.Profile.Name),
                ("Version", container.Profile.Setup.Version),
                ("Loader", container.Profile.Setup.Loader),
                ("Source", sourcePath)
            );
            output.WriteSuccess($"Instance {key.Key} imported.");
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
