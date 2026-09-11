using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;

namespace TridentCore.Cli.Commands;

public class PatchCommand(InstanceContextResolver resolver, CliOutput output)
    : InstanceCommandBase<PatchCommand.Arguments>(resolver)
{
    protected override int Execute(CommandContext context, Arguments settings, CancellationToken cancellationToken)
    {
        var instance = ResolveInstance(settings);
        if (settings.Action == "remove")
        {
            output.RequireConfirmation("Remove this patch and its owned assets?", settings.Yes);
        }
        var index = PatchOperation.ExecuteAsync(instance.Key, settings.Action, settings.Layer,
            settings.Path, settings.Name, settings.Position, cancellationToken).GetAwaiter().GetResult();
        if (output.UseStructuredOutput)
        {
            output.WriteData(new { instance = instance.Key, patches = index });
        }
        else
        {
            var table = new Table().AddColumn("Layer").AddColumn("Position").AddColumn("Patch").AddColumn("Enabled");
            foreach (var (layer, entries) in new[] { ("import", index.Import), ("users", index.Users) })
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    table.AddRow(layer, i.ToString(), Markup.Escape(entries[i].Path), CliOutput.FormatBoolean(entries[i].Enabled));
                }
            }
            output.WriteTable(table);
        }
        return ExitCodes.SUCCESS;
    }

    public class Arguments : InstanceArgumentsBase
    {
        [CommandArgument(0, "[ACTION]")]
        public string Action { get; set; } = "list";

        [CommandOption("--layer <LAYER>")]
        public string Layer { get; set; } = "users";

        [CommandOption("--path <PATH>")]
        public string? Path { get; set; }

        [CommandOption("--name <NAME>")]
        public string? Name { get; set; }

        [CommandOption("--position <POSITION>")]
        public int? Position { get; set; }

        [CommandOption("-y|--yes")]
        public bool Yes { get; set; }
    }
}
