using System.Globalization;
using Project27.Core;
using Project27.Core.Time;

namespace Project27.Cli;

/// <summary>Human-readable formatting; the inverse of the syntaxes in <see cref="Parsers"/>.</summary>
internal static class Render
{
    public static string Date(DateTime? value)
        => value?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "";

    public static string Time(TimeOnly value) => value.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string DateOnly(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Num(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    public static string? MinutesAsDays(decimal? minutes, TimeSettings settings)
        => minutes is null ? null : Num(minutes.Value / settings.MinutesPerDay) + "d";

    public static string DayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "mon",
        DayOfWeek.Tuesday => "tue",
        DayOfWeek.Wednesday => "wed",
        DayOfWeek.Thursday => "thu",
        DayOfWeek.Friday => "fri",
        DayOfWeek.Saturday => "sat",
        DayOfWeek.Sunday => "sun",
        _ => throw new ArgumentOutOfRangeException(nameof(day)),
    };

    public static string TypeAbbreviation(DependencyType type) => type switch
    {
        DependencyType.FinishToStart => "FS",
        DependencyType.StartToStart => "SS",
        DependencyType.FinishToFinish => "FF",
        DependencyType.StartToFinish => "SF",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>Leaf durations verbatim; summary durations are rolled-up minutes shown in days.</summary>
    public static string DurationText(ProjectTask task, TimeSettings settings)
        => task.IsSummary
            ? Duration.FromMinutes(task.DurationMinutes, DurationUnit.Days, settings).ToString()
            : task.Duration.ToString();

    /// <summary>Lag for data output: `2d`, `-1ed`, `50%`; null when zero.</summary>
    public static string? LagText(Lag lag, TimeSettings settings)
        => lag.IsZero ? null : LagWithSign(lag, settings, explicitPlus: false);

    /// <summary>MSP-style predecessor token: `3`, `3FS+2d`, `5SS-50%`.</summary>
    public static string PredecessorToken(TaskDependency dependency, TimeSettings settings)
    {
        var row = dependency.Predecessor.RowNumber.ToString(CultureInfo.InvariantCulture);
        if (dependency.Type == DependencyType.FinishToStart && dependency.Lag.IsZero)
        {
            return row;
        }

        var lag = dependency.Lag.IsZero ? "" : LagWithSign(dependency.Lag, settings, explicitPlus: true);
        return row + TypeAbbreviation(dependency.Type) + lag;
    }

    private static string LagWithSign(Lag lag, TimeSettings settings, bool explicitPlus)
    {
        var (value, suffix) = lag.Kind switch
        {
            LagKind.Working => (lag.Value / settings.MinutesPerDay, "d"),
            LagKind.Elapsed => (lag.Value / TimeInterval.MinutesPerDay, "ed"),
            LagKind.Percent => (lag.Value, "%"),
            _ => throw new ArgumentOutOfRangeException(nameof(lag)),
        };
        var sign = value < 0 ? "-" : explicitPlus ? "+" : "";
        return sign + Num(Math.Abs(value)) + suffix;
    }

    public static string ScheduleText(DaySchedule schedule)
        => schedule.IsWorking
            ? string.Join(",", schedule.Intervals.Select(i => i.ToString()))
            : "off";

    /// <summary>Round-trips through <see cref="Parsers.RecurrenceInput"/>.</summary>
    public static string RecurrenceSpec(Recurrence recurrence)
    {
        return recurrence switch
        {
            DailyRecurrence d => d.EveryDays == 1 ? "daily" : $"daily:{N(d.EveryDays)}",
            WeeklyRecurrence w => "weekly:" + (w.EveryWeeks == 1 ? "" : N(w.EveryWeeks) + ":") + DaysOf(w.Days),
            MonthlyDayRecurrence m => $"monthly-day:{N(m.Day)}" + (m.EveryMonths == 1 ? "" : ":" + N(m.EveryMonths)),
            MonthlyWeekdayRecurrence m => $"monthly-weekday:{Ordinal(m.Ordinal)}:{DayName(m.Weekday)}"
                + (m.EveryMonths == 1 ? "" : ":" + N(m.EveryMonths)),
            YearlyDateRecurrence y => $"yearly-date:{y.Month.ToString("00", CultureInfo.InvariantCulture)}-{y.Day.ToString("00", CultureInfo.InvariantCulture)}",
            YearlyWeekdayRecurrence y => $"yearly-weekday:{Ordinal(y.Ordinal)}:{DayName(y.Weekday)}:{N(y.Month)}",
            _ => throw new ArgumentOutOfRangeException(nameof(recurrence)),
        };

        static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

        static string DaysOf(DayOfWeekSet set)
            => string.Join(",", Enum.GetValues<DayOfWeek>().Where(day => set.Contains(day)).Select(DayName));

        static string Ordinal(WeekOrdinal ordinal) => ordinal switch
        {
            WeekOrdinal.First => "first",
            WeekOrdinal.Second => "second",
            WeekOrdinal.Third => "third",
            WeekOrdinal.Fourth => "fourth",
            WeekOrdinal.Last => "last",
            _ => throw new ArgumentOutOfRangeException(nameof(ordinal)),
        };
    }

    /// <summary>Aligned columns, two-space gutter, dash separator under the header.</summary>
    public static void Table(TextWriter writer, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var widths = headers.Select(h => h.Length).ToArray();
        foreach (var row in rows)
        {
            for (var i = 0; i < widths.Length; i++)
            {
                widths[i] = Math.Max(widths[i], row[i].Length);
            }
        }

        WriteRow(headers);
        writer.WriteLine(string.Join("  ", widths.Select(w => new string('-', w))));
        foreach (var row in rows)
        {
            WriteRow(row);
        }

        void WriteRow(IReadOnlyList<string> cells)
            => writer.WriteLine(string.Join("  ", cells.Select((cell, i) => cell.PadRight(widths[i]))).TrimEnd());
    }

    public static void KeyValues(TextWriter writer, IReadOnlyList<(string Key, string Value)> pairs)
    {
        var width = pairs.Max(p => p.Key.Length) + 1;
        foreach (var (key, value) in pairs)
        {
            writer.WriteLine($"{(key + ":").PadRight(width + 1)} {value}");
        }
    }
}
