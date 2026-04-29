using System.Text.Json;
using Spectre.Console;

namespace Trident.Cli.Services;

public class CliOutput(CliContext context)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public bool UseStructuredOutput => context.UseStructuredOutput;
    public bool IsInteractive => context.IsInteractive;

    public void WriteData<T>(T value)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
    }

    public void WriteMessage(string message)
    {
        if (UseStructuredOutput)
        {
            WriteData(new { message });
            return;
        }

        Console.Out.WriteLine(message);
    }

    public void WriteTable(Table table)
    {
        AnsiConsole.Write(table);
    }

    public void RequireConfirmation(string prompt, bool confirmed)
    {
        if (confirmed)
        {
            return;
        }

        if (!IsInteractive)
        {
            throw new CliException(
                "Confirmation is required. Re-run with --yes in non-interactive mode.",
                ExitCodes.Canceled
            );
        }

        if (!AnsiConsole.Confirm(prompt, false))
        {
            throw new CliException("Operation canceled.", ExitCodes.Canceled);
        }
    }
}
