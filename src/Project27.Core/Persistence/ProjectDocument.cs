using Project27.Core.Time;

namespace Project27.Core.Persistence;

/// <summary>
/// Serialization-friendly snapshot of a project (inputs only — schedule outputs are
/// recomputed on load). This is the storage format contract; bump
/// <see cref="SchemaVersion"/> on breaking shape changes.
/// </summary>
public sealed record ProjectDocument
{
    public int SchemaVersion { get; init; } = 7;

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

    /// <summary>Absent in schema-1 documents; defaults keep them loadable.</summary>
    public IReadOnlyList<ResourceDocument> Resources { get; init; } = [];

    public IReadOnlyList<AssignmentDocument> Assignments { get; init; } = [];

    // Schema 3.
    public DateTime? StatusDate { get; init; }

    // Schema 4.
    public IReadOnlyList<CustomFieldDocument> CustomFields { get; init; } = [];
}

public sealed record IndicatorRuleDocument(string Operator, string Value, string Icon);

public sealed record CustomFieldDocument
{
    public required string Id { get; init; }

    public string? Alias { get; init; }

    public string? Formula { get; init; }

    public IReadOnlyList<IndicatorRuleDocument>? Indicators { get; init; }
}

/// <summary>A stored custom value; <c>Value</c> is invariant text parsed by the slot's kind.</summary>
public sealed record CustomValueDocument(string Field, string Value);

public sealed record TaskBaselineDocument(
    int Slot,
    DateTime? Start,
    DateTime? Finish,
    decimal DurationMinutes,
    decimal WorkMinutes,
    decimal Cost);

public sealed record AssignmentBaselineDocument(int Slot, decimal WorkMinutes, decimal Cost);

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

    // Schema 2 (defaults preserve schema-1 semantics).
    public TaskType Type { get; init; }

    public bool IsEffortDriven { get; init; }

    public bool IgnoresResourceCalendars { get; init; }

    public decimal FixedCost { get; init; }

    public CostAccrual FixedCostAccrual { get; init; } = CostAccrual.Prorated;

    // Schema 3 (defaults preserve older semantics).
    public int PercentComplete { get; init; }

    public DateTime? ActualStart { get; init; }

    public DateTime? ActualFinish { get; init; }

    public IReadOnlyList<TaskBaselineDocument>? Baselines { get; init; }

    // Schema 4.
    public IReadOnlyList<CustomValueDocument>? CustomValues { get; init; }

    // Schema 5.
    public decimal LevelingDelayMinutes { get; init; }

    // Schema 6.
    public TaskFormattingDocument? Formatting { get; init; }
}

/// <summary>Cosmetic, non-scheduling display attributes; see <see cref="Core.TaskFormatting"/>.</summary>
public sealed record TaskFormattingDocument(int SpaceAfter);

public sealed record RateDocument(decimal Amount, RateUnit Per);

public sealed record CostRateDocument
{
    public required DateTime EffectiveFrom { get; init; }

    public required RateDocument StandardRate { get; init; }

    public required RateDocument OvertimeRate { get; init; }

    public decimal CostPerUse { get; init; }
}

public sealed record RateTableDocument
{
    public required CostRateTableId Table { get; init; }

    public required IReadOnlyList<CostRateDocument> Entries { get; init; }
}

public sealed record ResourceDocument
{
    public required Guid Id { get; init; }

    public required int UniqueId { get; init; }

    public required string Name { get; init; }

    public ResourceType Type { get; init; }

    public string? Initials { get; init; }

    public string? Group { get; init; }

    public decimal MaxUnits { get; init; } = 1m;

    public string? MaterialLabel { get; init; }

    public CostAccrual Accrual { get; init; } = CostAccrual.Prorated;

    public Guid? CalendarId { get; init; }

    /// <summary>Only tables that differ from the all-zero default are stored.</summary>
    public IReadOnlyList<RateTableDocument> RateTables { get; init; } = [];
}

public sealed record AssignmentDocument
{
    public required Guid Id { get; init; }

    public required Guid TaskId { get; init; }

    public required Guid ResourceId { get; init; }

    public decimal Units { get; init; } = 1m;

    public decimal WorkMinutes { get; init; }

    public WorkContour Contour { get; init; }

    public decimal DelayMinutes { get; init; }

    public CostRateTableId RateTable { get; init; }

    public decimal CostInput { get; init; }

    public IReadOnlyList<AssignmentBaselineDocument>? Baselines { get; init; }

    // Schema 7 (defaults preserve older semantics).
    /// <summary>Set = variable material consumption: Units are consumed per this time unit.</summary>
    public RateUnit? MaterialRateUnit { get; init; }

    /// <summary>Explicit actual work in minutes; null = derived from percent complete.</summary>
    public decimal? ActualWorkMinutes { get; init; }

    /// <summary>Explicit actual cost; null = derived from percent complete.</summary>
    public decimal? ActualCost { get; init; }
}

public sealed record DependencyDocument
{
    public required Guid PredecessorId { get; init; }

    public required Guid SuccessorId { get; init; }

    public DependencyType Type { get; init; }

    public LagKind LagKind { get; init; }

    public decimal LagValue { get; init; }
}
