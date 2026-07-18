using System.Globalization;
using Project27.Core;
using Project27.Core.Time;

namespace Project27.Cli;

/// <summary>Value syntaxes shared across verbs (see docs/spec/03-persistence-cli.md §2.2).</summary>
internal static class Parsers
{
    private static readonly string[] DateTimeFormats =
    [
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd H:mm",
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss",
    ];

    private static readonly string[] TimeFormats = ["HH:mm", "H:mm"];

    /// <summary>
    /// `yyyy-MM-dd[ HH:mm]`; a date-only value gets the project's default start time,
    /// or the default end time for finish-like fields (deadline, finish constraints…).
    /// </summary>
    public static DateTime DateInput(string text, TimeSettings settings, bool finishLike)
    {
        var trimmed = text.Trim();
        if (DateTime.TryParseExact(trimmed, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
        {
            return timestamp;
        }

        if (DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date.ToDateTime(finishLike ? settings.DefaultEndTime : settings.DefaultStartTime);
        }

        throw new CliException($"invalid date '{text}'; expected yyyy-MM-dd or yyyy-MM-dd HH:mm");
    }

    public static DateOnly DateOnlyInput(string text)
        => DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new CliException($"invalid date '{text}'; expected yyyy-MM-dd");

    public static TimeOnly TimeInput(string text)
        => TimeOnly.TryParseExact(text.Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : throw new CliException($"invalid time '{text}'; expected HH:mm");

    /// <summary>Bare integer = row number; `uid:N` = stable unique id.</summary>
    public static ProjectTask TaskRef(Project project, string reference)
    {
        var text = reference.Trim();
        if (text.StartsWith("uid:", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(text.AsSpan(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var uid))
            {
                return project.Tasks.FirstOrDefault(t => t.UniqueId == uid)
                    ?? throw new CliException($"no task with uid {uid}");
            }
        }
        else if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var row))
        {
            return project.Tasks.FirstOrDefault(t => t.RowNumber == row)
                ?? throw new CliException($"no task with id {row}");
        }

        throw new CliException($"invalid task reference '{reference}'; use a row id or uid:<n>");
    }

    public static Duration DurationInput(string text)
        => Duration.TryParse(text, out var duration)
            ? duration
            : throw new CliException($"invalid duration '{text}'; examples: 3d, 2.5w, 30m, 4eh, 1d?");

    /// <summary>A duration (`2d`, `4eh`), a percentage (`50%`), or either with leading `-` for lead.</summary>
    public static Lag LagInput(string text, TimeSettings settings)
    {
        var trimmed = text.Trim();
        if (trimmed is "0" or "")
        {
            return Lag.Zero;
        }

        var lead = trimmed[0] == '-';
        if (lead)
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.EndsWith('%'))
        {
            if (!decimal.TryParse(trimmed[..^1], NumberStyles.Number, CultureInfo.InvariantCulture, out var percent))
            {
                throw new CliException($"invalid lag '{text}'");
            }

            return Lag.Percent(lead ? -percent : percent);
        }

        return Lag.OfDuration(DurationInput(trimmed), settings, lead);
    }

    public static DependencyType DependencyTypeInput(string text) => text.Trim().ToUpperInvariant() switch
    {
        "FS" => DependencyType.FinishToStart,
        "SS" => DependencyType.StartToStart,
        "FF" => DependencyType.FinishToFinish,
        "SF" => DependencyType.StartToFinish,
        _ => throw new CliException($"invalid dependency type '{text}'; use fs, ss, ff, or sf"),
    };

    public static ConstraintType ConstraintInput(string text) => text.Trim().ToUpperInvariant() switch
    {
        "ASAP" => ConstraintType.AsSoonAsPossible,
        "ALAP" => ConstraintType.AsLateAsPossible,
        "SNET" => ConstraintType.StartNoEarlierThan,
        "SNLT" => ConstraintType.StartNoLaterThan,
        "FNET" => ConstraintType.FinishNoEarlierThan,
        "FNLT" => ConstraintType.FinishNoLaterThan,
        "MSO" => ConstraintType.MustStartOn,
        "MFO" => ConstraintType.MustFinishOn,
        _ => throw new CliException($"invalid constraint '{text}'; use asap, alap, snet, snlt, fnet, fnlt, mso, or mfo"),
    };

    public static DayOfWeek DayOfWeekInput(string text) => text.Trim().ToUpperInvariant() switch
    {
        "MON" or "MONDAY" => DayOfWeek.Monday,
        "TUE" or "TUESDAY" => DayOfWeek.Tuesday,
        "WED" or "WEDNESDAY" => DayOfWeek.Wednesday,
        "THU" or "THURSDAY" => DayOfWeek.Thursday,
        "FRI" or "FRIDAY" => DayOfWeek.Friday,
        "SAT" or "SATURDAY" => DayOfWeek.Saturday,
        "SUN" or "SUNDAY" => DayOfWeek.Sunday,
        _ => throw new CliException($"invalid day of week '{text}'"),
    };

    /// <summary>`off`, `inherit` (returns null), or intervals `08:00-12:00,13:00-17:00`.</summary>
    public static DaySchedule? DayScheduleInput(string text)
    {
        var trimmed = text.Trim();
        if (string.Equals(trimmed, "inherit", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(trimmed, "off", StringComparison.OrdinalIgnoreCase))
        {
            return DaySchedule.NonWorking;
        }

        var intervals = trimmed.Split(',').Select(part =>
        {
            var bounds = part.Split('-');
            if (bounds.Length != 2)
            {
                throw new CliException($"invalid working hours '{part}'; expected HH:mm-HH:mm");
            }

            return TimeInterval.FromTimes(TimeInput(bounds[0]), TimeInput(bounds[1]));
        }).ToArray();
        return DaySchedule.Working(intervals);
    }

    public static WeekOrdinal WeekOrdinalInput(string text) => text.Trim().ToUpperInvariant() switch
    {
        "FIRST" => WeekOrdinal.First,
        "SECOND" => WeekOrdinal.Second,
        "THIRD" => WeekOrdinal.Third,
        "FOURTH" => WeekOrdinal.Fourth,
        "LAST" => WeekOrdinal.Last,
        _ => throw new CliException($"invalid week ordinal '{text}'; use first, second, third, fourth, or last"),
    };

    /// <summary>Colon-separated recurrence spec shared by recurring tasks and calendar exceptions.</summary>
    public static Recurrence RecurrenceInput(string text)
    {
        var parts = text.Trim().Split(':');
        try
        {
            switch (parts[0].ToUpperInvariant())
            {
                case "DAILY" when parts.Length <= 2:
                    return new DailyRecurrence(parts.Length == 2 ? Int(parts[1]) : 1);
                case "WEEKLY" when parts.Length is 2 or 3:
                {
                    var every = parts.Length == 3 ? Int(parts[1]) : 1;
                    var days = parts[^1].Split(',')
                        .Select(DayOfWeekInput)
                        .Aggregate(DayOfWeekSet.None, (set, day) => set | day.AsSet());
                    return new WeeklyRecurrence(every, days);
                }

                case "MONTHLY-DAY" when parts.Length is 2 or 3:
                    return new MonthlyDayRecurrence(Int(parts[1]), parts.Length == 3 ? Int(parts[2]) : 1);
                case "MONTHLY-WEEKDAY" when parts.Length is 3 or 4:
                    return new MonthlyWeekdayRecurrence(
                        WeekOrdinalInput(parts[1]),
                        DayOfWeekInput(parts[2]),
                        parts.Length == 4 ? Int(parts[3]) : 1);
                case "YEARLY-DATE" when parts.Length == 2:
                {
                    var monthDay = parts[1].Split('-');
                    if (monthDay.Length != 2)
                    {
                        break;
                    }

                    return new YearlyDateRecurrence(Int(monthDay[0]), Int(monthDay[1]));
                }

                case "YEARLY-WEEKDAY" when parts.Length == 4:
                    return new YearlyWeekdayRecurrence(WeekOrdinalInput(parts[1]), DayOfWeekInput(parts[2]), Int(parts[3]));
                default:
                    break;
            }
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new CliException($"invalid recurrence '{text}': {exception.Message}", exception);
        }

        throw new CliException(
            $"invalid recurrence '{text}'; examples: daily:2, weekly:mon,fri, weekly:2:mon, "
            + "monthly-day:15, monthly-weekday:first:mon, yearly-date:07-04, yearly-weekday:last:fri:12");

        static int Int(string part)
            => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : throw new CliException($"'{part}' is not a number");
    }

    public static WorkCalendar CalendarByName(Project project, string name)
    {
        var matches = project.Calendars
            .Where(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new CliException($"no calendar named '{name}'"),
            _ => throw new CliException($"calendar name '{name}' is ambiguous"),
        };
    }

    public static bool BoolInput(string text) => text.Trim().ToUpperInvariant() switch
    {
        "TRUE" or "YES" or "ON" or "1" => true,
        "FALSE" or "NO" or "OFF" or "0" => false,
        _ => throw new CliException($"invalid boolean '{text}'; use true or false"),
    };

    /// <summary>Resource by name (case-insensitive, unique per project) or `uid:N`.</summary>
    public static Resource ResourceRef(Project project, string reference)
    {
        var text = reference.Trim();
        if (text.StartsWith("uid:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(text.AsSpan(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var uid))
        {
            return project.Resources.FirstOrDefault(r => r.UniqueId == uid)
                ?? throw new CliException($"no resource with uid {uid}");
        }

        return project.Resources.FirstOrDefault(r => string.Equals(r.Name, text, StringComparison.OrdinalIgnoreCase))
            ?? throw new CliException($"no resource named '{reference}'");
    }

    public static ResourceType ResourceTypeInput(string text) => text.Trim().ToUpperInvariant() switch
    {
        "WORK" => ResourceType.Work,
        "MATERIAL" => ResourceType.Material,
        "COST" => ResourceType.Cost,
        _ => throw new CliException($"invalid resource type '{text}'; use work, material, or cost"),
    };

    public static TaskType TaskTypeInput(string text) => text.Trim().ToUpperInvariant() switch
    {
        "FIXED-UNITS" => TaskType.FixedUnits,
        "FIXED-DURATION" => TaskType.FixedDuration,
        "FIXED-WORK" => TaskType.FixedWork,
        _ => throw new CliException($"invalid task type '{text}'; use fixed-units, fixed-duration, or fixed-work"),
    };

    public static WorkContour ContourInput(string text) => text.Trim().ToUpperInvariant() switch
    {
        "FLAT" => WorkContour.Flat,
        "BACK-LOADED" => WorkContour.BackLoaded,
        "FRONT-LOADED" => WorkContour.FrontLoaded,
        "DOUBLE-PEAK" => WorkContour.DoublePeak,
        "EARLY-PEAK" => WorkContour.EarlyPeak,
        "LATE-PEAK" => WorkContour.LatePeak,
        "BELL" => WorkContour.Bell,
        "TURTLE" => WorkContour.Turtle,
        _ => throw new CliException(
            $"invalid contour '{text}'; use flat, back-loaded, front-loaded, double-peak, early-peak, late-peak, bell, or turtle"),
    };

    public static CostAccrual AccrualInput(string text) => text.Trim().ToUpperInvariant() switch
    {
        "START" => CostAccrual.Start,
        "PRORATED" => CostAccrual.Prorated,
        "END" => CostAccrual.End,
        _ => throw new CliException($"invalid accrual '{text}'; use start, prorated, or end"),
    };

    public static CostRateTableId RateTableInput(string text) => text.Trim().ToUpperInvariant() switch
    {
        "A" => CostRateTableId.A,
        "B" => CostRateTableId.B,
        "C" => CostRateTableId.C,
        "D" => CostRateTableId.D,
        "E" => CostRateTableId.E,
        _ => throw new CliException($"invalid rate table '{text}'; use A..E"),
    };

    /// <summary>`50%` or a plain multiplier `0.5`; both mean half-time.</summary>
    public static decimal UnitsInput(string text)
    {
        var trimmed = text.Trim();
        var percent = trimmed.EndsWith('%');
        if (percent)
        {
            trimmed = trimmed[..^1];
        }

        if (!decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            throw new CliException($"invalid units '{text}'; examples: 50%, 200%, 1.5");
        }

        return percent ? value / 100m : value;
    }

    /// <summary>Rates: `50`, `50/h`, `400/d`… Material resources take a plain per-unit amount.</summary>
    public static Rate RateInput(string text, ResourceType type)
    {
        if (type == ResourceType.Material && text.Contains('/', StringComparison.Ordinal))
        {
            throw new CliException($"material rates are per unit; '{text}' must be a plain amount");
        }

        try
        {
            return Rate.Parse(text);
        }
        catch (FormatException exception)
        {
            throw new CliException(exception.Message, exception);
        }
    }

    /// <summary>Time base for variable material consumption: `h`, `d`, `w`, `mo`, `y`.</summary>
    public static RateUnit RateUnitInput(string text) => text.Trim().ToUpperInvariant() switch
    {
        "H" or "HR" or "HOUR" => RateUnit.Hour,
        "D" or "DAY" => RateUnit.Day,
        "W" or "WK" or "WEEK" => RateUnit.Week,
        "MO" or "MON" or "MONTH" => RateUnit.Month,
        "Y" or "YR" or "YEAR" => RateUnit.Year,
        _ => throw new CliException($"invalid time unit '{text}'; use h, d, w, mo, or y"),
    };

    public static decimal MoneyInput(string text)
        => decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new CliException($"invalid amount '{text}'");
}
