using Spectre.Console;

namespace Trident.Cli.Services;

public static class TableExtensions
{
    public static Table AddEscapedRow(this Table table, params string?[] columns) =>
        table.AddRow(columns.Select(x => Markup.Escape(x ?? string.Empty)).ToArray());

    public static Table AddMarkupRow(this Table table, params string[] columns) => table.AddRow(columns);

    public static Table AddEmptyRow(this Table table, int columnCount, string message)
    {
        var cells = Enumerable.Repeat(string.Empty, columnCount).ToArray();
        cells[0] = $"[dim]{Markup.Escape(message)}[/]";
        return table.AddRow(cells);
    }
}
