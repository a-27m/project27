using System.Globalization;
using Project27.Core.Time;

namespace Project27.Core.Fields;

/// <summary>Drives formatting, comparison, and filter-literal parsing of a field's raw values.</summary>
public enum FieldKind
{
    Text,
    WholeNumber,
    Number,
    Percent,
    Cost,

    /// <summary>Decimal working minutes, rendered in hours.</summary>
    Work,

    /// <summary>Decimal working minutes, rendered in days.</summary>
    Duration,
    Date,
    Flag,
}

/// <summary>One displayable task attribute (docs/spec/09-views-fields.md §9a).</summary>
public sealed record FieldDefinition(string Key, string Caption, FieldKind Kind, Func<ProjectTask, object?> Accessor);

/// <summary>
/// The built-in task field catalog. Raw values are string / int / decimal /
/// DateTime? / bool; durations, work, and slack are decimal minutes.
/// </summary>
public static class FieldCatalog
{
    private static readonly Dictionary<string, FieldDefinition> Fields = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<FieldDefinition> All => Fields.Values;

    static FieldCatalog()
    {
        // Identity & outline.
        Add("id", "ID", FieldKind.WholeNumber, t => t.RowNumber);
        Add("uid", "Unique ID", FieldKind.WholeNumber, t => t.UniqueId);
        Add("name", "Name", FieldKind.Text, t => t.Name);
        Add("wbs", "WBS", FieldKind.Text, t => t.Wbs);
        Add("outlineLevel", "Outline Level", FieldKind.WholeNumber, t => t.OutlineLevel);
        Add("summary", "Summary", FieldKind.Flag, t => t.IsSummary);
        Add("milestone", "Milestone", FieldKind.Flag, t => t.IsMilestone);
        Add("critical", "Critical", FieldKind.Flag, t => t.IsCritical);
        Add("active", "Active", FieldKind.Flag, t => t.IsActive);
        Add("mode", "Task Mode", FieldKind.Text, t => t.Mode == TaskMode.Auto ? "auto" : "manual");
        Add("type", "Type", FieldKind.Text, t => t.Type switch
        {
            TaskType.FixedUnits => "fixed-units",
            TaskType.FixedDuration => "fixed-duration",
            _ => "fixed-work",
        });
        Add("priority", "Priority", FieldKind.WholeNumber, t => t.Priority);

        // Scheduling.
        Add("duration", "Duration", FieldKind.Duration, t => t.DurationMinutes);
        Add("start", "Start", FieldKind.Date, t => t.Start);
        Add("finish", "Finish", FieldKind.Date, t => t.Finish);
        Add("earlyStart", "Early Start", FieldKind.Date, t => t.EarlyStart);
        Add("earlyFinish", "Early Finish", FieldKind.Date, t => t.EarlyFinish);
        Add("lateStart", "Late Start", FieldKind.Date, t => t.LateStart);
        Add("lateFinish", "Late Finish", FieldKind.Date, t => t.LateFinish);
        Add("totalSlack", "Total Slack", FieldKind.Duration, t => t.TotalSlackMinutes);
        Add("freeSlack", "Free Slack", FieldKind.Duration, t => t.FreeSlackMinutes);
        Add("constraint", "Constraint Type", FieldKind.Text, t => t.Constraint.ToString());
        Add("constraintDate", "Constraint Date", FieldKind.Date, t => t.ConstraintDate);
        Add("deadline", "Deadline", FieldKind.Date, t => t.Deadline);
        Add("calendar", "Task Calendar", FieldKind.Text, t => t.Calendar?.Name);
        Add("predecessors", "Predecessors", FieldKind.Text, PredecessorTokens);
        Add("resourceNames", "Resource Names", FieldKind.Text, ResourceNames);

        // Work & cost.
        Add("work", "Work", FieldKind.Work, t => t.WorkMinutes);
        Add("cost", "Cost", FieldKind.Cost, t => t.Cost);
        Add("fixedCost", "Fixed Cost", FieldKind.Cost, t => t.FixedCost);

        // Tracking.
        Add("percentComplete", "% Complete", FieldKind.Percent, t => t.PercentComplete);
        Add("actualStart", "Actual Start", FieldKind.Date, t => t.ActualStart);
        Add("actualFinish", "Actual Finish", FieldKind.Date, t => t.ActualFinish);
        Add("remainingDuration", "Remaining Duration", FieldKind.Duration, t => t.RemainingMinutes);
        Add("levelingDelay", "Leveling Delay", FieldKind.Duration, t => t.LevelingDelayMinutes);

        // Baseline 0 & variances.
        Add("baselineStart", "Baseline Start", FieldKind.Date, t => t.Baseline()?.Start);
        Add("baselineFinish", "Baseline Finish", FieldKind.Date, t => t.Baseline()?.Finish);
        Add("baselineDuration", "Baseline Duration", FieldKind.Duration, t => t.Baseline()?.DurationMinutes);
        Add("baselineWork", "Baseline Work", FieldKind.Work, t => t.Baseline()?.WorkMinutes);
        Add("baselineCost", "Baseline Cost", FieldKind.Cost, t => t.Baseline()?.Cost);
        Add("startVariance", "Start Variance", FieldKind.Duration, t => DateVariance(t, b => b.Start, t.Start));
        Add("finishVariance", "Finish Variance", FieldKind.Duration, t => DateVariance(t, b => b.Finish, t.Finish));
        Add("durationVariance", "Duration Variance", FieldKind.Duration, t => t.Baseline() is { } b ? t.DurationMinutes - b.DurationMinutes : null);
        Add("workVariance", "Work Variance", FieldKind.Work, t => t.Baseline() is { } b ? t.WorkMinutes - b.WorkMinutes : null);
        Add("costVariance", "Cost Variance", FieldKind.Cost, t => t.Baseline() is { } b ? t.Cost - b.Cost : null);

        // Earned value.
        Add("bac", "BAC", FieldKind.Cost, t => EarnedValue.ForTask(t).Bac);
        Add("bcws", "BCWS", FieldKind.Cost, t => EarnedValue.ForTask(t).Bcws);
        Add("bcwp", "BCWP", FieldKind.Cost, t => EarnedValue.ForTask(t).Bcwp);
        Add("acwp", "ACWP", FieldKind.Cost, t => EarnedValue.ForTask(t).Acwp);
        Add("sv", "SV", FieldKind.Cost, t => EarnedValue.ForTask(t).Sv);
        Add("cv", "CV", FieldKind.Cost, t => EarnedValue.ForTask(t).Cv);
        Add("spi", "SPI", FieldKind.Number, t => EarnedValue.ForTask(t).Spi);
        Add("cpi", "CPI", FieldKind.Number, t => EarnedValue.ForTask(t).Cpi);
        Add("eac", "EAC", FieldKind.Cost, t => EarnedValue.ForTask(t).Eac);
        Add("vac", "VAC", FieldKind.Cost, t => EarnedValue.ForTask(t).Vac);
    }

    private static void Add(string key, string caption, FieldKind kind, Func<ProjectTask, object?> accessor)
        => Fields.Add(key, new FieldDefinition(key, caption, kind, accessor));

    /// <summary>
    /// Field by key: built-ins, custom field slot ids, custom aliases, and the
    /// virtual `<field>.icon` indicator projection.
    /// </summary>
    public static FieldDefinition Resolve(Project project, string key)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var trimmed = key.Trim();
        if (Fields.TryGetValue(trimmed, out var definition))
        {
            return definition;
        }

        if (trimmed.EndsWith(".icon", StringComparison.OrdinalIgnoreCase)
            && project.FindCustomField(trimmed[..^5]) is { } indicatorField)
        {
            return new FieldDefinition(
                trimmed,
                indicatorField.Caption + " Icon",
                FieldKind.Text,
                task => EvaluateIndicator(indicatorField, task));
        }

        if (project.FindCustomField(trimmed) is { } custom)
        {
            return ToDefinition(custom);
        }

        throw new KeyNotFoundException($"Unknown field '{key}'.");
    }

    /// <summary>True when the key names a built-in field (aliases must not shadow these).</summary>
    public static bool IsBuiltin(string key) => Fields.ContainsKey(key.Trim());

    internal static FieldDefinition ToDefinition(CustomFieldDefinition custom)
        => new(custom.Id, custom.Caption, custom.Kind, task => CustomValue(custom, task));

    /// <summary>The stored or formula-computed value of a custom field for one task.</summary>
    public static object? CustomValue(CustomFieldDefinition field, ProjectTask task)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(task);
        if (field.ParsedFormula is not { } formula)
        {
            return task.GetCustomValue(field.Id);
        }

        var result = FormulaEvaluator.Evaluate(formula, task);
        return Coerce(field.Kind, result, field.Id);
    }

    /// <summary>First matching indicator rule's icon, or null.</summary>
    public static string? EvaluateIndicator(CustomFieldDefinition field, ProjectTask task)
    {
        ArgumentNullException.ThrowIfNull(field);
        var value = CustomValue(field, task);
        foreach (var rule in field.Indicators)
        {
            if (rule.Operator == Views.FilterOperator.Contains)
            {
                if (value?.ToString()?.Contains((string)rule.Value, StringComparison.OrdinalIgnoreCase) == true)
                {
                    return rule.Icon;
                }

                continue;
            }

            if (value is null)
            {
                continue;
            }

            var order = Compare(field.Kind, value, rule.Value);
            var matches = rule.Operator switch
            {
                Views.FilterOperator.Equals => order == 0,
                Views.FilterOperator.NotEquals => order != 0,
                Views.FilterOperator.GreaterThan => order > 0,
                Views.FilterOperator.GreaterOrEqual => order >= 0,
                Views.FilterOperator.LessThan => order < 0,
                Views.FilterOperator.LessOrEqual => order <= 0,
                _ => false,
            };
            if (matches)
            {
                return rule.Icon;
            }
        }

        return null;
    }

    private static object? Coerce(FieldKind kind, object? value, string fieldId)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return kind switch
            {
                FieldKind.Text => value as string ?? value.ToString(),
                FieldKind.Flag => (bool)value,
                FieldKind.Date => (DateTime)value,
                _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
            };
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
        {
            throw new InvalidOperationException(
                $"The formula of '{fieldId}' produced '{value}', which is not a {kind} value.", exception);
        }
    }

    /// <summary>Signed working minutes between baseline and current date (positive = later than planned).</summary>
    private static decimal? DateVariance(ProjectTask task, Func<TaskBaseline, DateTime?> baselineDate, DateTime? current)
    {
        if (task.Baseline() is not { } baseline || baselineDate(baseline) is not { } planned || current is not { } actual)
        {
            return null;
        }

        var calendar = task.Calendar ?? task.Project.Calendar;
        return calendar.WorkBetween(planned, actual);
    }

    private static string PredecessorTokens(ProjectTask task)
    {
        var settings = task.Project.TimeSettings;
        return string.Join(",", task.Predecessors.Select(d =>
        {
            var row = d.Predecessor.RowNumber.ToString(CultureInfo.InvariantCulture);
            if (d.Type == DependencyType.FinishToStart && d.Lag.IsZero)
            {
                return row;
            }

            var abbreviation = d.Type switch
            {
                DependencyType.FinishToStart => "FS",
                DependencyType.StartToStart => "SS",
                DependencyType.FinishToFinish => "FF",
                _ => "SF",
            };
            if (d.Lag.IsZero)
            {
                return row + abbreviation;
            }

            var (value, suffix) = d.Lag.Kind switch
            {
                LagKind.Working => (d.Lag.Value / settings.MinutesPerDay, "d"),
                LagKind.Elapsed => (d.Lag.Value / TimeInterval.MinutesPerDay, "ed"),
                _ => (d.Lag.Value, "%"),
            };
            var sign = value < 0 ? "-" : "+";
            return row + abbreviation + sign + Math.Abs(value).ToString("0.##", CultureInfo.InvariantCulture) + suffix;
        }));
    }

    private static string ResourceNames(ProjectTask task)
        => string.Join(", ", task.Assignments.Select(a =>
            a.Resource.Name + (a.Resource.Type == ResourceType.Work && a.Units != 1m
                ? $"[{(a.Units * 100m).ToString("0.##", CultureInfo.InvariantCulture)}%]"
                : "")));

    /// <summary>Human rendering of a raw field value.</summary>
    public static string Format(FieldKind kind, object? value, TimeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (value is null)
        {
            return "";
        }

        return kind switch
        {
            FieldKind.Text => (string)value,
            FieldKind.WholeNumber => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            FieldKind.Number => Num(value),
            FieldKind.Percent => Num(value) + "%",
            FieldKind.Cost => Num(value),
            FieldKind.Work => Num(Convert.ToDecimal(value, CultureInfo.InvariantCulture) / 60m) + "h",
            FieldKind.Duration => Num(Convert.ToDecimal(value, CultureInfo.InvariantCulture) / settings.MinutesPerDay) + "d",
            FieldKind.Date => ((DateTime)value).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            FieldKind.Flag => (bool)value ? "yes" : "no",
            _ => value.ToString() ?? "",
        };

        static string Num(object raw)
        {
            var rounded = Math.Round(Convert.ToDecimal(raw, CultureInfo.InvariantCulture), 2);
            return rounded.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Null-first ordering of raw values within one kind.</summary>
    public static int Compare(FieldKind kind, object? left, object? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null ? 0 : left is null ? -1 : 1;
        }

        return kind switch
        {
            FieldKind.Text => string.Compare((string)left, (string)right, StringComparison.OrdinalIgnoreCase),
            FieldKind.Date => DateTime.Compare((DateTime)left, (DateTime)right),
            FieldKind.Flag => ((bool)left).CompareTo((bool)right),
            _ => Convert.ToDecimal(left, CultureInfo.InvariantCulture)
                .CompareTo(Convert.ToDecimal(right, CultureInfo.InvariantCulture)),
        };
    }

    /// <summary>Parses a filter/formula literal according to the field's kind.</summary>
    public static object ParseLiteral(FieldKind kind, string text, TimeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var trimmed = text.Trim();
        return kind switch
        {
            FieldKind.Text => trimmed,
            FieldKind.Flag => trimmed.ToUpperInvariant() switch
            {
                "TRUE" or "YES" or "1" => true,
                "FALSE" or "NO" or "0" => false,
                _ => throw new FormatException($"'{text}' is not a flag value; use true or false."),
            },
            FieldKind.Date => DateTime.TryParseExact(
                trimmed,
                ["yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date)
                ? date
                : throw new FormatException($"'{text}' is not a date; use yyyy-MM-dd[ HH:mm]."),
            FieldKind.Duration or FieldKind.Work => Duration.TryParse(trimmed, out var duration)
                ? duration.ToMinutes(settings)
                : throw new FormatException($"'{text}' is not a duration; examples: 3d, 4h."),
            _ => decimal.TryParse(trimmed.TrimEnd('%'), NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                ? number
                : throw new FormatException($"'{text}' is not a number."),
        };
    }
}
