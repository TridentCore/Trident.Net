using Spectre.Console;

namespace Trident.Cli.Services;

public static class TableExtensions
{
    public static Table AddEscapedRow(this Table table, params string?[] columns) =>
        table.AddRow(columns.Select(x => Markup.Escape(x ?? string.Empty)).ToArray());
}
