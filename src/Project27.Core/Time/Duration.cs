using System.Globalization;

namespace Project27.Core.Time;

/// <summary>
/// A user-facing duration: non-negative value, unit, and an "estimated" flag (rendered
/// as a trailing <c>?</c>, e.g. <c>3d?</c>). Conversion to working minutes goes through
/// <see cref="TimeSettings"/>; elapsed units convert at fixed clock-time rates.
/// </summary>
public readonly record struct Duration
{
    private const decimal ElapsedMinutesPerDay = 24 * 60;
    private const decimal ElapsedMinutesPerWeek = 7 * 24 * 60;
    private const decimal ElapsedMinutesPerMonth = 30 * 24 * 60;

    private static readonly Dictionary<string, DurationUnit> UnitAliases = BuildUnitAliases();

    public Duration(decimal value, DurationUnit unit, bool isEstimated = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Value = value;
        Unit = unit;
        IsEstimated = isEstimated;
    }

    public decimal Value { get; }

    public DurationUnit Unit { get; }

    public bool IsEstimated { get; }

    public bool IsElapsed => Unit >= DurationUnit.ElapsedMinutes;

    public static Duration Zero { get; } = new(0, DurationUnit.Days);

    public decimal ToMinutes(TimeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Unit switch
        {
            DurationUnit.Minutes => Value,
            DurationUnit.Hours => Value * 60,
            DurationUnit.Days => Value * settings.MinutesPerDay,
            DurationUnit.Weeks => Value * settings.MinutesPerWeek,
            DurationUnit.Months => Value * settings.DaysPerMonth * settings.MinutesPerDay,
            DurationUnit.ElapsedMinutes => Value,
            DurationUnit.ElapsedHours => Value * 60,
            DurationUnit.ElapsedDays => Value * ElapsedMinutesPerDay,
            DurationUnit.ElapsedWeeks => Value * ElapsedMinutesPerWeek,
            DurationUnit.ElapsedMonths => Value * ElapsedMinutesPerMonth,
            _ => throw new InvalidOperationException($"Unknown duration unit {Unit}."),
        };
    }

    public static Duration FromMinutes(decimal minutes, DurationUnit unit, TimeSettings settings, bool isEstimated = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var perUnit = unit switch
        {
            DurationUnit.Minutes => 1m,
            DurationUnit.Hours => 60m,
            DurationUnit.Days => settings.MinutesPerDay,
            DurationUnit.Weeks => settings.MinutesPerWeek,
            DurationUnit.Months => settings.DaysPerMonth * settings.MinutesPerDay,
            DurationUnit.ElapsedMinutes => 1m,
            DurationUnit.ElapsedHours => 60m,
            DurationUnit.ElapsedDays => ElapsedMinutesPerDay,
            DurationUnit.ElapsedWeeks => ElapsedMinutesPerWeek,
            DurationUnit.ElapsedMonths => ElapsedMinutesPerMonth,
            _ => throw new ArgumentOutOfRangeException(nameof(unit)),
        };
        return new Duration(minutes / perUnit, unit, isEstimated);
    }

    public static Duration Parse(string text)
    {
        return TryParse(text, out var result)
            ? result
            : throw new FormatException($"'{text}' is not a valid duration. Expected e.g. '3d', '2.5 wks', '4ed', '1d?'.");
    }

    public static bool TryParse(string? text, out Duration result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.AsSpan().Trim();
        var isEstimated = false;
        if (span.Length > 0 && span[^1] == '?')
        {
            isEstimated = true;
            span = span[..^1].TrimEnd();
        }

        var digits = 0;
        while (digits < span.Length && (char.IsAsciiDigit(span[digits]) || span[digits] == '.'))
        {
            digits++;
        }

        if (digits == 0)
        {
            return false;
        }

        if (!decimal.TryParse(span[..digits], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        var unitToken = span[digits..].Trim();
        if (unitToken.IsEmpty || !UnitAliases.TryGetValue(unitToken.ToString(), out var unit))
        {
            return false;
        }

        result = new Duration(value, unit, isEstimated);
        return true;
    }

    public override string ToString() => ToString(DurationFormat.Compact);

    public string ToString(DurationFormat format)
    {
        var value = Value.ToString("0.##", CultureInfo.InvariantCulture);
        var unit = format == DurationFormat.Compact ? CompactUnitName(Unit) : VerboseUnitName(Unit, Value);
        var separator = format == DurationFormat.Compact ? string.Empty : " ";
        var estimated = IsEstimated ? "?" : string.Empty;
        return $"{value}{separator}{unit}{estimated}";
    }

    private static string CompactUnitName(DurationUnit unit) => unit switch
    {
        DurationUnit.Minutes => "m",
        DurationUnit.Hours => "h",
        DurationUnit.Days => "d",
        DurationUnit.Weeks => "w",
        DurationUnit.Months => "mo",
        DurationUnit.ElapsedMinutes => "em",
        DurationUnit.ElapsedHours => "eh",
        DurationUnit.ElapsedDays => "ed",
        DurationUnit.ElapsedWeeks => "ew",
        DurationUnit.ElapsedMonths => "emo",
        _ => throw new ArgumentOutOfRangeException(nameof(unit)),
    };

    private static string VerboseUnitName(DurationUnit unit, decimal value)
    {
        var name = unit switch
        {
            DurationUnit.Minutes => "minute",
            DurationUnit.Hours => "hour",
            DurationUnit.Days => "day",
            DurationUnit.Weeks => "week",
            DurationUnit.Months => "month",
            DurationUnit.ElapsedMinutes => "elapsed minute",
            DurationUnit.ElapsedHours => "elapsed hour",
            DurationUnit.ElapsedDays => "elapsed day",
            DurationUnit.ElapsedWeeks => "elapsed week",
            DurationUnit.ElapsedMonths => "elapsed month",
            _ => throw new ArgumentOutOfRangeException(nameof(unit)),
        };
        return value == 1 ? name : name + "s";
    }

    private static Dictionary<string, DurationUnit> BuildUnitAliases()
    {
        var bases = new (DurationUnit Unit, string[] Aliases)[]
        {
            (DurationUnit.Minutes, ["m", "min", "mins", "minute", "minutes"]),
            (DurationUnit.Hours, ["h", "hr", "hrs", "hour", "hours"]),
            (DurationUnit.Days, ["d", "dy", "dys", "day", "days"]),
            (DurationUnit.Weeks, ["w", "wk", "wks", "week", "weeks"]),
            (DurationUnit.Months, ["mo", "mon", "mons", "month", "months"]),
        };
        var map = new Dictionary<string, DurationUnit>(StringComparer.OrdinalIgnoreCase);
        foreach (var (unit, aliases) in bases)
        {
            // Elapsed units are the same aliases with an "e" prefix, offset in the enum.
            var elapsed = unit + (DurationUnit.ElapsedMinutes - DurationUnit.Minutes);
            foreach (var alias in aliases)
            {
                map.Add(alias, unit);
                map.Add("e" + alias, elapsed);
            }
        }

        return map;
    }
}

public enum DurationFormat
{
    Compact,
    Verbose,
}
