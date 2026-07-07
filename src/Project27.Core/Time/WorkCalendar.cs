namespace Project27.Core.Time;

/// <summary>
/// A working-time calendar, optionally derived from a base calendar. Day resolution
/// order (walking the base chain most-derived first): exceptions, then work weeks that
/// define the weekday, then default week with per-day inheritance, else non-working.
/// See docs/spec/01-time-and-calendars.md.
/// </summary>
public sealed partial class WorkCalendar
{
    // Monotonic clock shared by all calendars: any mutation gives the calendar a globally
    // unique, increasing version, so a chain's cache stamp (max version in the chain)
    // changes on any edit, including re-basing onto a different calendar.
    private static long s_clock;

    private readonly List<CalendarException> _exceptions = [];
    private readonly List<WorkWeek> _workWeeks = [];
    private WeeklyPattern _defaultWeek;
    private WorkCalendar? _baseCalendar;
    private long _version;
    private Dictionary<DateOnly, DaySchedule> _dayCache = [];
    private long _dayCacheStamp = -1;

    public WorkCalendar(string name, WorkCalendar? baseCalendar = null, WeeklyPattern? defaultWeek = null, Guid? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id ?? Guid.NewGuid();
        Name = name;
        _baseCalendar = baseCalendar;
        _defaultWeek = defaultWeek ?? WeeklyPattern.InheritAll;
        Touch();
    }

    public Guid Id { get; }

    public string Name { get; set; }

    public WorkCalendar? BaseCalendar => _baseCalendar;

    public WeeklyPattern DefaultWeek
    {
        get => _defaultWeek;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _defaultWeek = value;
            Touch();
        }
    }

    public IReadOnlyList<CalendarException> Exceptions => _exceptions;

    public IReadOnlyList<WorkWeek> WorkWeeks => _workWeeks;

    public void SetBaseCalendar(WorkCalendar? baseCalendar)
    {
        for (var c = baseCalendar; c is not null; c = c._baseCalendar)
        {
            if (ReferenceEquals(c, this))
            {
                throw new InvalidOperationException($"Setting '{baseCalendar!.Name}' as base of '{Name}' would create a cycle.");
            }
        }

        _baseCalendar = baseCalendar;
        Touch();
    }

    /// <summary>Sets one weekday of the default week; null restores inheritance.</summary>
    public void SetDay(DayOfWeek day, DaySchedule? schedule) => DefaultWeek = _defaultWeek.With(day, schedule);

    public void AddException(CalendarException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _exceptions.Add(exception);
        Touch();
    }

    public bool RemoveException(CalendarException exception)
    {
        var removed = _exceptions.Remove(exception);
        if (removed)
        {
            Touch();
        }

        return removed;
    }

    public void AddWorkWeek(WorkWeek workWeek)
    {
        ArgumentNullException.ThrowIfNull(workWeek);
        _workWeeks.Add(workWeek);
        Touch();
    }

    public bool RemoveWorkWeek(WorkWeek workWeek)
    {
        var removed = _workWeeks.Remove(workWeek);
        if (removed)
        {
            Touch();
        }

        return removed;
    }

    /// <summary>Effective schedule for a date after exception/work-week/inheritance resolution. Cached.</summary>
    public DaySchedule GetDaySchedule(DateOnly date)
    {
        var stamp = ChainStamp;
        if (_dayCacheStamp != stamp)
        {
            _dayCache = [];
            _dayCacheStamp = stamp;
        }

        if (!_dayCache.TryGetValue(date, out var schedule))
        {
            schedule = Resolve(date);
            _dayCache[date] = schedule;
        }

        return schedule;
    }

    private DaySchedule Resolve(DateOnly date)
    {
        for (var c = this; c is not null; c = c._baseCalendar)
        {
            foreach (var exception in c._exceptions)
            {
                if (exception.AppliesTo(date))
                {
                    return exception.Schedule;
                }
            }
        }

        for (var c = this; c is not null; c = c._baseCalendar)
        {
            foreach (var workWeek in c._workWeeks)
            {
                if (workWeek.Covers(date) && workWeek.Pattern[date.DayOfWeek] is { } fromWorkWeek)
                {
                    return fromWorkWeek;
                }
            }
        }

        for (var c = this; c is not null; c = c._baseCalendar)
        {
            if (c._defaultWeek[date.DayOfWeek] is { } fromDefaultWeek)
            {
                return fromDefaultWeek;
            }
        }

        return DaySchedule.NonWorking;
    }

    private void Touch() => _version = Interlocked.Increment(ref s_clock);

    private long ChainStamp
    {
        get
        {
            var stamp = 0L;
            for (var c = this; c is not null; c = c._baseCalendar)
            {
                stamp = Math.Max(stamp, c._version);
            }

            return stamp;
        }
    }

    public static WorkCalendar CreateStandard(string name = "Standard")
    {
        var day = DaySchedule.Working(new TimeInterval(8 * 60, 12 * 60), new TimeInterval(13 * 60, 17 * 60));
        var week = WeeklyPattern.Create(
            sunday: DaySchedule.NonWorking,
            monday: day,
            tuesday: day,
            wednesday: day,
            thursday: day,
            friday: day,
            saturday: DaySchedule.NonWorking);
        return new WorkCalendar(name, defaultWeek: week);
    }

    public static WorkCalendar Create24Hours(string name = "24 Hours")
    {
        var allDay = DaySchedule.Working(new TimeInterval(0, TimeInterval.MinutesPerDay));
        var week = WeeklyPattern.Create(allDay, allDay, allDay, allDay, allDay, allDay, allDay);
        return new WorkCalendar(name, defaultWeek: week);
    }

    public static WorkCalendar CreateNightShift(string name = "Night Shift")
    {
        // 23:00–08:00 shifts Monday night through Saturday morning, 03:00–04:00 break.
        var lateStart = DaySchedule.Working(new TimeInterval(23 * 60, 24 * 60));
        var full = DaySchedule.Working(
            new TimeInterval(0, 3 * 60),
            new TimeInterval(4 * 60, 8 * 60),
            new TimeInterval(23 * 60, 24 * 60));
        var earlyEnd = DaySchedule.Working(
            new TimeInterval(0, 3 * 60),
            new TimeInterval(4 * 60, 8 * 60));
        var week = WeeklyPattern.Create(
            sunday: DaySchedule.NonWorking,
            monday: lateStart,
            tuesday: full,
            wednesday: full,
            thursday: full,
            friday: full,
            saturday: earlyEnd);
        return new WorkCalendar(name, defaultWeek: week);
    }
}
