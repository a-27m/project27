using Project27.Core.Time;

namespace Project27.Core.Persistence;

/// <summary>
/// Serialization-friendly snapshot of a project (inputs only — schedule outputs are
/// recomputed on load). This is the storage format contract; bump
/// <see cref="SchemaVersion"/> on breaking shape changes.
/// </summary>
public sealed record ProjectDocument
{
    public int SchemaVersion { get; init; } = 1;

    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required DateTime StartDate { get; init; }

    public DateTime? FinishDate { get; init; }

    public ScheduleFrom ScheduleFrom { get; init; }

    public required Guid CalendarId { get; init; }

    public required TimeSettingsDocument TimeSettings { get; init; }

    public decimal CriticalSlackThresholdMinutes { get; init; }

    public required IReadOnlyList<CalendarDocument> Calendars { get; init; }

    public required IReadOnlyList<TaskDocument> Tasks { get; init; }

    public required IReadOnlyList<DependencyDocument> Dependencies { get; init; }
}

public sealed record TimeSettingsDocument
{
    public required int MinutesPerDay { get; init; }

    public required int MinutesPerWeek { get; init; }

    public required decimal DaysPerMonth { get; init; }

    public required DayOfWeek WeekStartsOn { get; init; }

    public required TimeOnly DefaultStartTime { get; init; }

    public required TimeOnly DefaultEndTime { get; init; }
}

public sealed record IntervalDocument(int Start, int End);

/// <summary>A day's schedule; an empty interval list is an explicitly non-working day.</summary>
public sealed record DayDocument(IReadOnlyList<IntervalDocument> Intervals);

public sealed record RecurrenceDocument
{
    public required string Kind { get; init; }

    public int Every { get; init; } = 1;

    public int Day { get; init; }

    public int Month { get; init; }

    public DayOfWeekSet Days { get; init; }

    public WeekOrdinal Ordinal { get; init; }

    public DayOfWeek Weekday { get; init; }
}

public sealed record ExceptionDocument
{
    public required string Name { get; init; }

    public required DateOnly Start { get; init; }

    public DateOnly? End { get; init; }

    public int? Occurrences { get; init; }

    public required DayDocument Schedule { get; init; }

    public RecurrenceDocument? Recurrence { get; init; }
}

public sealed record WorkWeekDocument
{
    public required string Name { get; init; }

    public required DateOnly Start { get; init; }

    public required DateOnly End { get; init; }

    /// <summary>Seven entries, Sunday first; null = inherit.</summary>
    public required IReadOnlyList<DayDocument?> Days { get; init; }
}

public sealed record CalendarDocument
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public Guid? BaseCalendarId { get; init; }

    /// <summary>Seven entries, Sunday first; null = inherit.</summary>
    public required IReadOnlyList<DayDocument?> DefaultWeek { get; init; }

    public required IReadOnlyList<ExceptionDocument> Exceptions { get; init; }

    public required IReadOnlyList<WorkWeekDocument> WorkWeeks { get; init; }
}

public sealed record SplitPartDocument(decimal WorkMinutes, decimal GapMinutes);

public sealed record TaskDocument
{
    public required Guid Id { get; init; }

    public required int UniqueId { get; init; }

    public Guid? ParentId { get; init; }

    public required string Name { get; init; }

    public TaskMode Mode { get; init; }

    public bool IsActive { get; init; } = true;

    public required decimal DurationValue { get; init; }

    public required DurationUnit DurationUnit { get; init; }

    public bool IsEstimated { get; init; }

    public bool? MilestoneOverride { get; init; }

    public ConstraintType Constraint { get; init; }

    public DateTime? ConstraintDate { get; init; }

    public DateTime? Deadline { get; init; }

    public Guid? CalendarId { get; init; }

    public int Priority { get; init; } = 500;

    public DateTime? ManualStart { get; init; }

    public DateTime? ManualFinish { get; init; }

    public string? CustomWbs { get; init; }

    public bool IsRecurring { get; init; }

    public IReadOnlyList<SplitPartDocument>? SplitParts { get; init; }
}

public sealed record DependencyDocument
{
    public required Guid PredecessorId { get; init; }

    public required Guid SuccessorId { get; init; }

    public DependencyType Type { get; init; }

    public LagKind LagKind { get; init; }

    public decimal LagValue { get; init; }
}
