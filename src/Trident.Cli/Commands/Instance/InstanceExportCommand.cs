using Spectre.Console.Cli;
using Trident.Abstractions.FileModels;
using Trident.Cli.Services;
using Trident.Core.Services;

namespace Trident.Cli.Commands.Instance;

public class InstanceExportCommand(
    InstanceContextResolver resolver,
    ExporterAgent exporterAgent,
    CliOutput output
) : InstanceCommandBase<InstanceExportCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        ExportAsync(settings, cancellationToken).GetAwaiter().GetResult();
        return ExitCodes.Success;
    }

    private async Task ExportAsync(Arguments settings, CancellationToken cancellationToken)
    {
        var instance = ResolveInstance(settings);
        var options = PackData.CreateDefault();
        options.IncludingSource = string.Equals(
            settings.Type,
            "offline",
            StringComparison.OrdinalIgnoreCase
        );
        options.IncludingTags = !settings.NoTags;

        using var container = await exporterAgent
            .ExportAsync(
                options,
                settings.Format,
                instance.Key,
                settings.Name ?? instance.Profile.Name,
                settings.Author,
                settings.Version
            )
            .ConfigureAwait(false);
        await using var archive = await exporterAgent.PackCompressedAsync(container).ConfigureAwait(false);

        var outputPath = Path.GetFullPath(settings.Output);
        var dir = Path.GetDirectoryName(outputPath);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }

        await using var file = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        await archive.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        await file.FlushAsync(cancellationToken).ConfigureAwait(false);

        var result = new
        {
            action = "export",
            key = instance.Key,
            format = settings.Format,
            type = settings.Type,
            output = outputPath,
        };

        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
        }
        else
        {
            output.WriteMessage($"Instance {instance.Key} exported to {outputPath}.");
        }
    }

    public class Arguments : InstanceArgumentsBase
    {
        [CommandOption("--format <FORMAT>")]
        public string Format { get; set; } = "trident";

        [CommandOption("--type <TYPE>")]
        public string Type { get; set; } = "online";

        [CommandOption("--name <NAME>")]
        public string? Name { get; set; }

        [CommandOption("--author <AUTHOR>", true)]
        public required string Author { get; set; }

        [CommandOption("--version <VERSION>")]
        public string Version { get; set; } = "1.0.0";

        [CommandOption("--output <PATH>", true)]
        public required string Output { get; set; }

        [CommandOption("--no-tags")]
        public bool NoTags { get; set; }
    }
}
