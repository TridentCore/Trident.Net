using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Instance;

public class InstanceExportCommand(InstanceContextResolver resolver, ExporterAgent exporterAgent, CliOutput output)
    : InstanceCommandBase<InstanceExportCommand.Arguments>(resolver)
{
    protected override int Execute(CommandContext context, Arguments settings, CancellationToken cancellationToken)
    {
        var instance = ResolveInstance(settings);
        var result = InstanceOperation
                    .ExportAsync(Resolver,
                                 exporterAgent,
                                 instance.Key,
                                 settings.Profile,
                                 settings.Format,
                                 settings.Type,
                                 settings.Name,
                                 settings.Author,
                                 settings.Version,
                                 settings.Output,
                                 settings.NoTags)
                    .GetAwaiter()
                    .GetResult();

        if (output.UseStructuredOutput)
        {
            output.WriteData(new
            {
                action = "export",
                key = result.Key,
                format = result.Format,
                type = result.Type,
                output = result.Output
            });
        }
        else
        {
            var size = File.Exists(result.Output) ? new FileInfo(result.Output).Length : 0;
            output.WriteKeyValueTable("Instance exported",
                                      ("Instance", result.Key),
                                      ("Format", result.Format),
                                      ("Type", result.Type),
                                      ("Output", result.Output),
                                      ("Size", $"{size:n0} bytes"));
            output.WriteSuccess($"Instance {result.Key} exported.");
        }

        return ExitCodes.SUCCESS;
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
