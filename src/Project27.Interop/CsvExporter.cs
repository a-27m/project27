using System.Text;
using Project27.Core;
using Project27.Core.Fields;
using Project27.Core.Views;

namespace Project27.Interop;

/// <summary>
/// RFC-4180 CSV over the view engine (docs/spec/05-interop.md §5a): any table,
/// field list, filter, sort, or grouping; formatted cell values; CRLF line ends.
/// </summary>
public static class CsvExporter
{
    public static string Write(Project project, ViewDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(definition);
        var result = TaskView.Evaluate(project, definition);
        var grouped = definition.GroupBy is not null;
        var csv = new StringBuilder();

        AppendRow(csv, grouped
            ? ["Group", .. result.Fields.Select(f => f.Caption)]
            : result.Fields.Select(f => f.Caption));

        foreach (var group in result.Groups)
        {
            foreach (var row in group.Rows)
            {
                AppendRow(csv, grouped
                    ? [group.Heading ?? "", .. row.Cells.Select(c => c.Text)]
                    : row.Cells.Select(c => c.Text));
            }
        }

        return csv.ToString();
    }

    private static void AppendRow(StringBuilder csv, IEnumerable<string> cells)
        => csv.Append(string.Join(',', cells.Select(Quote))).Append("\r\n");

    private static string Quote(string cell)
        => cell.Contains(',', StringComparison.Ordinal)
            || cell.Contains('"', StringComparison.Ordinal)
            || cell.Contains('\n', StringComparison.Ordinal)
            || cell.Contains('\r', StringComparison.Ordinal)
            ? "\"" + cell.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : cell;

    /// <summary>Convenience: fields from a named table (default entry).</summary>
    public static IReadOnlyList<string> FieldsOf(string? table)
        => TaskView.Tables.TryGetValue(table ?? "entry", out var fields)
            ? fields
            : throw new KeyNotFoundException($"Unknown table '{table}'; use {string.Join(", ", TaskView.Tables.Keys)}.");

    internal static FieldDefinition ResolveField(Project project, string key) => FieldCatalog.Resolve(project, key);
}
