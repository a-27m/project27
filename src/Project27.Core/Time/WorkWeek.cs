namespace Project27.Core.Time;

/// <summary>
/// An alternate weekly pattern scoped to a date range (e.g. "summer hours").
/// Weekdays the pattern leaves undefined fall through to default-week resolution.
/// Immutable.
/// </summary>
public sealed class WorkWeek
{
    public WorkWeek(string name, DateOnly start, DateOnly end, WeeklyPattern pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(pattern);
        if (end < start)
        {
            throw new ArgumentException("Work week end date precedes its start date.", nameof(end));
        }

        Name = name;
        Start = start;
        End = end;
        Pattern = pattern;
    }

    public string Name { get; }

    public DateOnly Start { get; }

    public DateOnly End { get; }

    public WeeklyPattern Pattern { get; }

    public bool Covers(DateOnly date) => date >= Start && date <= End;
}
