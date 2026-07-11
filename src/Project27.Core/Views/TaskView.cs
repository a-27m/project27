using Project27.Core.Fields;

namespace Project27.Core.Views;

public sealed record SortKey(string Field, bool Descending);

/// <summary>What to show: which fields, filtered and ordered how.</summary>
public sealed record ViewDefinition(
    IReadOnlyList<string> Fields,
    FilterNode? Filter = null,
    IReadOnlyList<SortKey>? Sorts = null,
    string? GroupBy = null);

public sealed record ViewCell(string Field, object? Raw, string Text);

public sealed record ViewRow(ProjectTask Task, IReadOnlyList<ViewCell> Cells);

/// <summary>Rows under one group heading; a single unnamed group when ungrouped.</summary>
public sealed record ViewGroup(string? Heading, IReadOnlyList<ViewRow> Rows);

public sealed record ViewResult(IReadOnlyList<FieldDefinition> Fields, IReadOnlyList<ViewGroup> Groups);

/// <summary>
/// Evaluates view definitions against the task list. Outline order is kept when
/// unsorted/ungrouped; sorting or grouping flattens to leaf tasks (deviation #25).
/// </summary>
public static class TaskView
{
    /// <summary>Built-in tables: named field selections (deviation #24).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Tables { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["entry"] = ["id", "name", "duration", "start", "finish", "predecessors", "resourceNames"],
            ["schedule"] = ["id", "name", "start", "finish", "lateStart", "lateFinish", "freeSlack", "totalSlack"],
            ["cost"] = ["id", "name", "fixedCost", "cost", "baselineCost", "costVariance"],
            ["work"] = ["id", "name", "work", "baselineWork", "workVariance", "percentComplete"],
            ["tracking"] = ["id", "name", "actualStart", "actualFinish", "percentComplete", "remainingDuration"],
            ["variance"] = ["id", "name", "start", "finish", "baselineStart", "baselineFinish", "startVariance", "finishVariance"],
            ["evm"] = ["id", "name", "bcws", "bcwp", "acwp", "sv", "cv", "spi", "cpi", "bac", "eac", "vac"],
            ["summary"] = ["id", "name", "duration", "start", "finish", "percentComplete", "cost", "work"],
        };

    public static ViewResult Evaluate(Project project, ViewDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(definition);
        var fields = definition.Fields.Select(key => FieldCatalog.Resolve(project, key)).ToList();
        var settings = project.TimeSettings;

        var flatten = definition.GroupBy is not null || definition.Sorts is { Count: > 0 };
        IEnumerable<ProjectTask> tasks = project.Tasks;
        if (flatten)
        {
            tasks = tasks.Where(t => !t.IsSummary);
        }

        if (definition.Filter is { } filter)
        {
            tasks = tasks.Where(filter.Matches);
        }

        if (definition.Sorts is { Count: > 0 } sorts)
        {
            var keys = sorts.Select(s => (Definition: FieldCatalog.Resolve(project, s.Field), s.Descending)).ToList();
            tasks = tasks.Order(Comparer<ProjectTask>.Create((left, right) =>
            {
                foreach (var (fieldDefinition, descending) in keys)
                {
                    var order = FieldCatalog.Compare(fieldDefinition.Kind, fieldDefinition.Accessor(left), fieldDefinition.Accessor(right));
                    if (order != 0)
                    {
                        return descending ? -order : order;
                    }
                }

                return left.RowNumber.CompareTo(right.RowNumber);
            }));
        }

        List<ViewGroup> groups;
        if (definition.GroupBy is { } groupKey)
        {
            var groupField = FieldCatalog.Resolve(project, groupKey);
            groups = [.. tasks
                .GroupBy(t => groupField.Accessor(t))
                .OrderBy(g => g.Key, Comparer<object?>.Create((a, b) => FieldCatalog.Compare(groupField.Kind, a, b)))
                .Select(g => new ViewGroup(
                    $"{groupField.Caption}: {(g.Key is null ? "(none)" : FieldCatalog.Format(groupField.Kind, g.Key, settings))}",
                    [.. g.Select(Row)]))];
        }
        else
        {
            groups = [new ViewGroup(null, [.. tasks.Select(Row)])];
        }

        return new ViewResult(fields, groups);

        ViewRow Row(ProjectTask task) => new(
            task,
            [.. fields.Select(f =>
            {
                var raw = f.Accessor(task);
                return new ViewCell(f.Key, raw, FieldCatalog.Format(f.Kind, raw, settings));
            })]);
    }

    /// <summary>Parses `"finish desc, name"` into sort keys.</summary>
    public static IReadOnlyList<SortKey> ParseSorts(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return [.. text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part =>
            {
                var pieces = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var descending = pieces.Length > 1 && pieces[1].Equals("desc", StringComparison.OrdinalIgnoreCase);
                if (pieces.Length > 2 || (pieces.Length == 2 && !descending && !pieces[1].Equals("asc", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new FormatException($"Invalid sort '{part}'; use \"field [asc|desc]\".");
                }

                return new SortKey(pieces[0], descending);
            })];
    }
}
