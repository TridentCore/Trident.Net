using System.Text.Json;
using Spectre.Console;

namespace TridentCore.Cli.Services;

public class CliOutput(CliContext context)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public bool UseStructuredOutput => context.UseStructuredOutput;
    public bool IsInteractive => context.IsInteractive;
    public bool CanUseRichOutput => !UseStructuredOutput;

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

    public void WriteInfo(string message) =>
        WriteMarkupLine($"[blue]INFO[/] {Markup.Escape(message)}");

    public void WriteSuccess(string message) =>
        WriteMarkupLine($"[green]OK[/] {Markup.Escape(message)}");

    public void WriteWarning(string message) =>
        WriteMarkupLine($"[yellow]WARN[/] {Markup.Escape(message)}");

    public void WriteError(string message)
    {
        if (UseStructuredOutput)
        {
            WriteData(new { error = message });
            return;
        }

        AnsiConsole.Write(
            new Panel($"[bold red]{Markup.Escape(message)}[/]")
                .Header("[bold red]ERROR[/]")
                .RoundedBorder()
                .BorderColor(Color.Red)
                .Expand()
        );
    }

    public void WriteMarkupLine(string markup)
    {
        if (UseStructuredOutput)
        {
            WriteData(new { message = Markup.Remove(markup) });
            return;
        }

        AnsiConsole.MarkupLine(markup);
    }

    public void WriteTable(Table table)
    {
        AnsiConsole.Write(table);
    }

    public void WriteKeyValueTable(string title, params (string Key, string? Value)[] rows)
    {
        var table = new Table().RoundedBorder().HideHeaders();
        table.Title = new($"[bold]{Markup.Escape(title)}[/]");
        table.AddColumn("Field");
        table.AddColumn("Value");
        foreach (var row in rows)
        {
            table.AddRow($"[cyan]{Markup.Escape(row.Key)}[/]", FormatValue(row.Value));
        }

        WriteTable(table);
    }

    public void WriteEmptyState(string title, string hint)
    {
        AnsiConsole.Write(
            new Panel($"[bold]{Markup.Escape(title)}[/]\n[dim]{Markup.Escape(hint)}[/]")
                .RoundedBorder()
                .BorderColor(Color.Grey)
        );
    }

    public async Task StatusAsync(string message, Func<Task> action)
    {
        if (!IsInteractive || UseStructuredOutput)
        {
            await action().ConfigureAwait(false);
            return;
        }

        await AnsiConsole
            .Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync(message, async _ => await action().ConfigureAwait(false))
            .ConfigureAwait(false);
    }

    public async Task<T> StatusAsync<T>(string message, Func<Task<T>> action)
    {
        if (!IsInteractive || UseStructuredOutput)
        {
            return await action().ConfigureAwait(false);
        }

        return await AnsiConsole
            .Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync(message, async _ => await action().ConfigureAwait(false))
            .ConfigureAwait(false);
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

        if (!AnsiConsole.Confirm($"[yellow]{Markup.Escape(prompt)}[/]", false))
        {
            throw new CliException("Operation canceled.", ExitCodes.Canceled);
        }
    }

    public static string FormatValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "[dim]-[/]" : Markup.Escape(value);

    public static string FormatBoolean(
        bool value,
        string trueLabel = "yes",
        string falseLabel = "no"
    ) => value ? $"[green]{Markup.Escape(trueLabel)}[/]" : $"[dim]{Markup.Escape(falseLabel)}[/]";

    public static string FormatStatus(string value, string color) =>
        $"[{color}]{Markup.Escape(value)}[/]";
}
