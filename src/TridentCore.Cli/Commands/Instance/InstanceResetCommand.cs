using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Abstractions;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Instance;

public class InstanceResetCommand(
    InstanceContextResolver resolver,
    InstanceManager instanceManager,
    CliOutput output
) : InstanceCommandBase<InstanceResetCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var instance = ResolveInstance(settings);
        if (instanceManager.IsInUse(instance.Key))
        {
            throw new CliException($"Instance '{instance.Key}' is currently in use.", ExitCodes.Usage);
        }

        output.RequireConfirmation($"Reset build artifacts for instance '{instance.Key}'?", settings.Yes);

        var deleted = new List<string>();
        DeleteDirectory(PathDef.Default.DirectoryOfBuild(instance.Key), deleted);
        DeleteDirectory(PathDef.Default.DirectoryOfLive(instance.Key), deleted);
        DeleteFile(PathDef.Default.FileOfLockData(instance.Key), deleted);

        var result = new { action = "reset", key = instance.Key, deleted };
        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
        }
        else
        {
            output.WriteKeyValueTable(
                "Instance reset",
                ("Instance", instance.Key),
                ("Deleted Items", deleted.Count.ToString())
            );
            if (deleted.Count > 0)
            {
                var table = new Table().RoundedBorder();
                table.Title = new TableTitle("[bold]Deleted paths[/]");
                table.AddColumn("Path");
                foreach (var path in deleted)
                {
                    table.AddEscapedRow(path);
                }

                output.WriteTable(table);
            }

            output.WriteSuccess($"Instance {instance.Key} reset.");
        }

        return ExitCodes.Success;
    }

    private static void DeleteDirectory(string path, IList<string> deleted)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Directory.Delete(path, true);
        deleted.Add(path);
    }

    private static void DeleteFile(string path, IList<string> deleted)
    {
        if (!File.Exists(path))
        {
            return;
        }

        File.Delete(path);
        deleted.Add(path);
    }

    public class Arguments : InstanceArgumentsBase
    {
        [CommandOption("-y|--yes")]
        public bool Yes { get; set; }
    }
}
