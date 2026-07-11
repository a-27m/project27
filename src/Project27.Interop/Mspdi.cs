using System.Globalization;
using System.Xml.Linq;
using Project27.Core;
using Project27.Core.Time;

namespace Project27.Interop;

/// <summary>Shared MSPDI vocabulary (docs/spec/05-interop.md §5b).</summary>
internal static class Mspdi
{
    public static readonly XNamespace Ns = "http://schemas.microsoft.com/project";

    public const string DateFormat = "yyyy-MM-ddTHH:mm:ss";

    public static string Date(DateTime value) => value.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static DateTime ParseDate(string text)
        => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    /// <summary>Working minutes as MSPDI's ISO-8601-ish PT{h}H{m}M{s}S.</summary>
    public static string Duration(decimal minutes)
    {
        var totalSeconds = (long)Math.Round(minutes * 60m);
        var hours = totalSeconds / 3600;
        var remaining = totalSeconds % 3600;
        return string.Create(CultureInfo.InvariantCulture, $"PT{hours}H{remaining / 60}M{remaining % 60}S");
    }

    public static decimal ParseDuration(string text)
    {
        // PT8H0M0S — tolerate missing components.
        var span = text.AsSpan();
        if (!span.StartsWith("PT", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"'{text}' is not an MSPDI duration.");
        }

        decimal minutes = 0;
        var number = 0m;
        var fraction = 0m;
        foreach (var character in span[2..])
        {
            if (char.IsDigit(character))
            {
                if (fraction > 0)
                {
                    number += (character - '0') * fraction;
                    fraction /= 10m;
                }
                else
                {
                    number = (number * 10m) + (character - '0');
                }
            }
            else if (character == '.')
            {
                fraction = 0.1m;
            }
            else
            {
                minutes += char.ToUpperInvariant(character) switch
                {
                    'H' => number * 60m,
                    'M' => number,
                    'S' => number / 60m,
                    _ => throw new InvalidDataException($"'{text}' is not an MSPDI duration."),
                };
                number = 0m;
                fraction = 0m;
            }
        }

        return minutes;
    }

    // MSPDI ConstraintType: 0 ASAP, 1 ALAP, 2 MSO, 3 MFO, 4 SNET, 5 SNLT, 6 FNET, 7 FNLT.
    public static int ConstraintCode(ConstraintType type) => type switch
    {
        ConstraintType.AsSoonAsPossible => 0,
        ConstraintType.AsLateAsPossible => 1,
        ConstraintType.MustStartOn => 2,
        ConstraintType.MustFinishOn => 3,
        ConstraintType.StartNoEarlierThan => 4,
        ConstraintType.StartNoLaterThan => 5,
        ConstraintType.FinishNoEarlierThan => 6,
        _ => 7,
    };

    public static ConstraintType ConstraintFromCode(int code) => code switch
    {
        1 => ConstraintType.AsLateAsPossible,
        2 => ConstraintType.MustStartOn,
        3 => ConstraintType.MustFinishOn,
        4 => ConstraintType.StartNoEarlierThan,
        5 => ConstraintType.StartNoLaterThan,
        6 => ConstraintType.FinishNoEarlierThan,
        7 => ConstraintType.FinishNoLaterThan,
        _ => ConstraintType.AsSoonAsPossible,
    };

    // MSPDI link Type: 0 FF, 1 FS, 2 SF, 3 SS.
    public static int LinkCode(DependencyType type) => type switch
    {
        DependencyType.FinishToFinish => 0,
        DependencyType.FinishToStart => 1,
        DependencyType.StartToFinish => 2,
        _ => 3,
    };

    public static DependencyType LinkFromCode(int code) => code switch
    {
        0 => DependencyType.FinishToFinish,
        2 => DependencyType.StartToFinish,
        3 => DependencyType.StartToStart,
        _ => DependencyType.FinishToStart,
    };

    // Task Type: 0 fixed units, 1 fixed duration, 2 fixed work.
    public static int TaskTypeCode(TaskType type) => type switch
    {
        TaskType.FixedUnits => 0,
        TaskType.FixedDuration => 1,
        _ => 2,
    };

    public static TaskType TaskTypeFromCode(int code) => code switch
    {
        1 => TaskType.FixedDuration,
        2 => TaskType.FixedWork,
        _ => TaskType.FixedUnits,
    };

    // AccrueAt: 1 start, 2 end, 3 prorated.
    public static int AccrualCode(CostAccrual accrual) => accrual switch
    {
        CostAccrual.Start => 1,
        CostAccrual.End => 2,
        _ => 3,
    };

    public static CostAccrual AccrualFromCode(int code) => code switch
    {
        1 => CostAccrual.Start,
        2 => CostAccrual.End,
        _ => CostAccrual.Prorated,
    };

    public static string Flag(bool value) => value ? "1" : "0";

    public static string Num(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    public static string TimeOfDay(int minutes)
        => System.TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minutes)).ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    public static int MinutesOfDay(string text)
        => (int)System.TimeOnly.ParseExact(text, "HH:mm:ss", CultureInfo.InvariantCulture).ToTimeSpan().TotalMinutes;

    public static XElement E(string name, params object?[] content) => new(Ns + name, content);

    public static string? Text(XElement parent, string name) => parent.Element(Ns + name)?.Value;

    public static int Int(XElement parent, string name, int fallback = 0)
        => int.TryParse(Text(parent, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    public static decimal Dec(XElement parent, string name, decimal fallback = 0m)
        => decimal.TryParse(Text(parent, name), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    public static bool Bool(XElement parent, string name, bool fallback = false)
        => Text(parent, name) switch { "1" or "true" => true, "0" or "false" => false, _ => fallback };

    public static DateTime? DateOrNull(XElement parent, string name)
        => Text(parent, name) is { Length: > 0 } text ? ParseDate(text) : null;

    /// <summary>Applies a working-minute lag from tenths + format (5/6 = elapsed).</summary>
    public static Lag LagFrom(int tenthsOfMinutes, int lagFormat)
    {
        if (tenthsOfMinutes == 0)
        {
            return Lag.Zero;
        }

        var minutes = tenthsOfMinutes / 10m;
        return Lag.Restore(lagFormat is 5 or 6 ? LagKind.Elapsed : LagKind.Working, minutes);
    }
}
