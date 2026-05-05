using Spectre.Console.Cli;
using TridentCore.Abstractions.FileModels;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Instance;

public class InstanceExportCommand(InstanceContextResolver resolver, ExporterAgent exporterAgent, CliOutput output)
    : InstanceCommandBase<InstanceExportCommand.Arguments>(resolver)
{
    protected override int Execute(CommandContext context, Arguments settings, CancellationToken cancellationToken)
    {
        ExportAsync(settings, cancellationToken).GetAwaiter().GetResult();
        return ExitCodes.Success;
    }

    private async Task ExportAsync(Arguments settings, CancellationToken cancellationToken)
    {
        var instance = ResolveInstance(settings);
        var options = new PackData
        {
            IncludingSource = string.Equals(settings.Type, "offline", StringComparison.OrdinalIgnoreCase),
            IncludingTags = !settings.NoTags,
            OfflineMode = string.Equals(settings.Type, "offline", StringComparison.OrdinalIgnoreCase),
        };

        using var container = await output
                                   .StatusAsync("Collecting export data...",
                                                async () => await exporterAgent
                                                                 .ExportAsync(options,
                                                                              settings.Format,
                                                                              instance.Key,
                                                                              settings.Name ?? instance.Profile.Name,
                                                                              settings.Author,
                                                                              settings.Version)
                                                                 .ConfigureAwait(false))
                                   .ConfigureAwait(false);

        var outputPath = Path.GetFullPath(settings.Output);
        var dir = Path.GetDirectoryName(outputPath);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }

        await output
             .StatusAsync("Packing and writing archive...",
                          async () =>
                          {
                              await using var file = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                              await exporterAgent.PackCompressedAsync(file, container).ConfigureAwait(false);
                              await file.FlushAsync(cancellationToken).ConfigureAwait(false);
                          })
             .ConfigureAwait(false);

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
            var size = File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0;
            output.WriteKeyValueTable("Instance exported",
                                      ("Instance", instance.Key),
                                      ("Format", settings.Format),
                                      ("Type", settings.Type),
                                      ("Output", outputPath),
                                      ("Size", $"{size:n0} bytes"));
            output.WriteSuccess($"Instance {instance.Key} exported.");
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
