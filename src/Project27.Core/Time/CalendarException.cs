namespace Project27.Core.Time;

/// <summary>
/// A calendar exception: a date span (holiday, shutdown) or a recurrence, replacing the
/// day's schedule with <see cref="Schedule"/> (non-working by default). Recurring
/// exceptions end at <see cref="End"/> or after <see cref="Occurrences"/> occurrences.
/// Immutable.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "'Calendar exception' is MS Project's established domain term; this is not a System.Exception.")]
public sealed class CalendarException
{
    // Bound for count-limited recurrences with no end date.
    private const int MaxHorizonYears = 100;

    private HashSet<DateOnly>? _dates;

    public CalendarException(
        string name,
        DateOnly start,
        DateOnly? end = null,
        DaySchedule schedule = default,
        Recurrence? recurrence = null,
        int? occurrences = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (end is { } e && e < start)
        {
            throw new ArgumentException("Exception end date precedes its start date.", nameof(end));
        }

        if (occurrences is { } n)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(n, 1, nameof(occurrences));
            if (recurrence is null)
            {
                throw new ArgumentException("An occurrence count requires a recurrence pattern.", nameof(occurrences));
            }
        }

        Name = name;
        Start = start;
        End = end;
        Schedule = schedule;
        Recurrence = recurrence;
        Occurrences = occurrences;
    }

    public string Name { get; }

    public DateOnly Start { get; }

    /// <summary>Inclusive end of the window; null means single day (no recurrence) or count/horizon-bounded (with recurrence).</summary>
    public DateOnly? End { get; }

    public DaySchedule Schedule { get; }

    public Recurrence? Recurrence { get; }

    public int? Occurrences { get; }

    public bool AppliesTo(DateOnly date)
    {
        if (date < Start)
        {
            return false;
        }

        if (Recurrence is null)
        {
            return date <= (End ?? Start);
        }

        if (End is { } end && date > end)
        {
            return false;
        }

        return Dates.Contains(date);
    }

    private HashSet<DateOnly> Dates
    {
        get
        {
            if (_dates is null)
            {
                var until = End ?? Start.AddYears(MaxHorizonYears);
                var occurrences = Recurrence!.Occurrences(Start, until);
                if (Occurrences is { } count)
                {
                    occurrences = occurrences.Take(count);
                }

                _dates = [.. occurrences];
            }

            return _dates;
        }
    }
}
