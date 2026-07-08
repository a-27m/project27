using System.Globalization;
using Project27.Core.Time;

namespace Project27.Core;

public enum LagKind
{
    /// <summary>Working time on the successor's calendar.</summary>
    Working,

    /// <summary>Clock time, ignoring calendars.</summary>
    Elapsed,

    /// <summary>Percentage of the predecessor's duration, applied as working time.</summary>
    Percent,
}

/// <summary>
/// Dependency lag. Positive delays the successor, negative is lead. Time kinds carry
/// minutes; <see cref="LagKind.Percent"/> carries percentage points.
/// </summary>
public readonly record struct Lag
{
    private Lag(LagKind kind, decimal value)
    {
        Kind = kind;
        Value = value;
    }

    public LagKind Kind { get; }

    public decimal Value { get; }

    public bool IsZero => Value == 0;

    public static Lag Zero { get; } = OfMinutes(0);

    public static Lag OfMinutes(decimal minutes) => new(LagKind.Working, minutes);

    public static Lag OfDuration(Duration duration, TimeSettings settings, bool lead = false)
    {
        var minutes = duration.ToMinutes(settings);
        return new Lag(duration.IsElapsed ? LagKind.Elapsed : LagKind.Working, lead ? -minutes : minutes);
    }

    public static Lag ElapsedMinutes(decimal minutes) => new(LagKind.Elapsed, minutes);

    public static Lag Percent(decimal percent) => new(LagKind.Percent, percent);

    /// <summary>Reconstructs a lag from persisted raw values.</summary>
    public static Lag Restore(LagKind kind, decimal value) => new(kind, value);

    public override string ToString() => Kind switch
    {
        LagKind.Percent => string.Create(CultureInfo.InvariantCulture, $"{Value}%"),
        LagKind.Elapsed => string.Create(CultureInfo.InvariantCulture, $"{Value}em"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{Value}m"),
    };
}
