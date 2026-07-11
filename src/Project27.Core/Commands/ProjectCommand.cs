using System.Text.Json.Serialization;

namespace Project27.Core.Commands;

/// <summary>
/// A serializable fine-grained mutation (docs/spec/07-web-foundation.md). Tasks are
/// addressed by stable uid; durations use engine syntax ("3d", "4eh"); absent
/// members mean "unchanged" and clearing uses explicit flags. Applied by
/// <see cref="CommandExecutor"/>; inverse commands (undo/redo) arrive in phase 12.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "op")]
[JsonDerivedType(typeof(AddTaskCommand), "addTask")]
[JsonDerivedType(typeof(SetTaskCommand), "setTask")]
[JsonDerivedType(typeof(RemoveTaskCommand), "removeTask")]
[JsonDerivedType(typeof(MoveTaskCommand), "moveTask")]
[JsonDerivedType(typeof(IndentTaskCommand), "indentTask")]
[JsonDerivedType(typeof(OutdentTaskCommand), "outdentTask")]
[JsonDerivedType(typeof(LinkCommand), "link")]
[JsonDerivedType(typeof(SetLinkCommand), "setLink")]
[JsonDerivedType(typeof(UnlinkCommand), "unlink")]
[JsonDerivedType(typeof(SplitTaskCommand), "splitTask")]
[JsonDerivedType(typeof(UnsplitTaskCommand), "unsplitTask")]
[JsonDerivedType(typeof(SetProjectCommand), "setProject")]
[JsonDerivedType(typeof(SetBaselineCommand), "setBaseline")]
[JsonDerivedType(typeof(ClearBaselineCommand), "clearBaseline")]
[JsonDerivedType(typeof(LevelCommand), "level")]
[JsonDerivedType(typeof(ClearLevelingCommand), "clearLeveling")]
[JsonDerivedType(typeof(AddResourceCommand), "addResource")]
[JsonDerivedType(typeof(SetResourceCommand), "setResource")]
[JsonDerivedType(typeof(RemoveResourceCommand), "removeResource")]
[JsonDerivedType(typeof(SetResourceRateCommand), "setResourceRate")]
[JsonDerivedType(typeof(RemoveResourceRateCommand), "removeResourceRate")]
[JsonDerivedType(typeof(AssignCommand), "assign")]
[JsonDerivedType(typeof(SetAssignmentCommand), "setAssignment")]
[JsonDerivedType(typeof(UnassignCommand), "unassign")]
[JsonDerivedType(typeof(AddCalendarCommand), "addCalendar")]
[JsonDerivedType(typeof(RemoveCalendarCommand), "removeCalendar")]
[JsonDerivedType(typeof(SetCalendarDayCommand), "setCalendarDay")]
[JsonDerivedType(typeof(SetCalendarBaseCommand), "setCalendarBase")]
[JsonDerivedType(typeof(AddCalendarExceptionCommand), "addCalendarException")]
[JsonDerivedType(typeof(RemoveCalendarExceptionCommand), "removeCalendarException")]
[JsonDerivedType(typeof(AddWorkWeekCommand), "addWorkWeek")]
[JsonDerivedType(typeof(RemoveWorkWeekCommand), "removeWorkWeek")]
[JsonDerivedType(typeof(DefineCustomFieldCommand), "defineCustomField")]
[JsonDerivedType(typeof(RemoveCustomFieldCommand), "removeCustomField")]
[JsonDerivedType(typeof(AddRecurringTaskCommand), "addRecurringTask")]
[JsonDerivedType(typeof(RescheduleCommand), "reschedule")]
public abstract record ProjectCommand;

public sealed record AddTaskCommand : ProjectCommand
{
    public required string Name { get; init; }

    /// <summary>Engine duration syntax; null = default (1d? estimated).</summary>
    public string? Duration { get; init; }

    public int? ParentUid { get; init; }

    /// <summary>0-based position under the parent; null = append.</summary>
    public int? At { get; init; }

    public bool Milestone { get; init; }
}

public sealed record CommandLag(LagKind Kind, decimal Value);

public sealed record SetTaskCommand : ProjectCommand
{
    public required int Uid { get; init; }

    public string? Name { get; init; }

    public string? Duration { get; init; }

    public TaskMode? Mode { get; init; }

    public bool? Active { get; init; }

    public bool? Milestone { get; init; }

    public int? Priority { get; init; }

    public DateTime? Deadline { get; init; }

    public bool ClearDeadline { get; init; }

    public ConstraintType? Constraint { get; init; }

    public DateTime? ConstraintDate { get; init; }

    /// <summary>Calendar by name; empty string is invalid, use <see cref="ClearCalendar"/>.</summary>
    public string? Calendar { get; init; }

    public bool ClearCalendar { get; init; }

    public string? Wbs { get; init; }

    public bool ClearWbs { get; init; }

    public DateTime? ManualStart { get; init; }

    public bool ClearManualStart { get; init; }

    public DateTime? ManualFinish { get; init; }

    public bool ClearManualFinish { get; init; }

    public TaskType? Type { get; init; }

    public bool? EffortDriven { get; init; }

    public decimal? FixedCost { get; init; }

    public CostAccrual? FixedCostAccrual { get; init; }

    public bool? IgnoreResourceCalendars { get; init; }

    public int? PercentComplete { get; init; }

    public DateTime? ActualStart { get; init; }

    public bool ClearActualStart { get; init; }

    public DateTime? ActualFinish { get; init; }

    public bool ClearActualFinish { get; init; }

    /// <summary>Engine duration syntax; rewrites the total keeping the completed span.</summary>
    public string? RemainingDuration { get; init; }

    /// <summary>Custom field values by slot id or alias; text parsed by the field's kind; null clears.</summary>
    public IReadOnlyDictionary<string, string?>? CustomValues { get; init; }
}

public sealed record RemoveTaskCommand : ProjectCommand
{
    public required int Uid { get; init; }
}

public sealed record MoveTaskCommand : ProjectCommand
{
    public required int Uid { get; init; }

    /// <summary>Null = top level.</summary>
    public int? ParentUid { get; init; }

    public required int At { get; init; }
}

public sealed record IndentTaskCommand : ProjectCommand
{
    public required int Uid { get; init; }
}

public sealed record OutdentTaskCommand : ProjectCommand
{
    public required int Uid { get; init; }
}

public sealed record LinkCommand : ProjectCommand
{
    public required int PredecessorUid { get; init; }

    public required int SuccessorUid { get; init; }

    public DependencyType Type { get; init; }

    public CommandLag? Lag { get; init; }
}

public sealed record SetLinkCommand : ProjectCommand
{
    public required int PredecessorUid { get; init; }

    public required int SuccessorUid { get; init; }

    public DependencyType? Type { get; init; }

    public CommandLag? Lag { get; init; }
}

public sealed record UnlinkCommand : ProjectCommand
{
    public required int PredecessorUid { get; init; }

    public required int SuccessorUid { get; init; }
}

public sealed record SplitTaskCommand : ProjectCommand
{
    public required int Uid { get; init; }

    /// <summary>Working-time offset from the task start, engine duration syntax.</summary>
    public required string At { get; init; }

    public required string Gap { get; init; }
}

public sealed record UnsplitTaskCommand : ProjectCommand
{
    public required int Uid { get; init; }
}

public sealed record SetProjectCommand : ProjectCommand
{
    public string? Name { get; init; }

    public DateTime? Start { get; init; }

    public ScheduleFrom? ScheduleFrom { get; init; }

    /// <summary>Project calendar by name.</summary>
    public string? Calendar { get; init; }

    public DateTime? StatusDate { get; init; }

    public bool ClearStatusDate { get; init; }

    public int? MinutesPerDay { get; init; }

    public int? MinutesPerWeek { get; init; }

    public decimal? DaysPerMonth { get; init; }

    public DayOfWeek? WeekStartsOn { get; init; }

    /// <summary>"HH:mm".</summary>
    public string? DayStart { get; init; }

    public string? DayEnd { get; init; }

    /// <summary>Engine duration syntax; total-slack threshold for criticality.</summary>
    public string? CriticalSlack { get; init; }
}

public sealed record SetBaselineCommand : ProjectCommand
{
    public int Slot { get; init; }

    /// <summary>Uids to capture; empty = the whole project.</summary>
    public IReadOnlyList<int> Uids { get; init; } = [];
}

public sealed record ClearBaselineCommand : ProjectCommand
{
    public int Slot { get; init; }

    public IReadOnlyList<int> Uids { get; init; } = [];
}

public sealed record LevelCommand : ProjectCommand;

public sealed record ClearLevelingCommand : ProjectCommand;

// ------------------------------------------------------------- 12p-1 ops
// Resources and calendars are addressed by (unique) name; assignments by
// (task uid, resource name). Structured payloads — the web builds forms,
// not text syntaxes.

public sealed record AddResourceCommand : ProjectCommand
{
    public required string Name { get; init; }

    public ResourceType Type { get; init; }

    public decimal? MaxUnits { get; init; }

    /// <summary>Rate syntax, e.g. "50/h"; per-unit amount for material.</summary>
    public string? Rate { get; init; }

    public string? MaterialLabel { get; init; }

    public string? Calendar { get; init; }

    public string? Initials { get; init; }

    public string? Group { get; init; }
}

public sealed record SetResourceCommand : ProjectCommand
{
    public required string Resource { get; init; }

    public string? Name { get; init; }

    public decimal? MaxUnits { get; init; }

    public string? MaterialLabel { get; init; }

    public string? Calendar { get; init; }

    public bool ClearCalendar { get; init; }

    public string? Initials { get; init; }

    public string? Group { get; init; }

    public CostAccrual? Accrual { get; init; }
}

public sealed record RemoveResourceCommand : ProjectCommand
{
    public required string Resource { get; init; }
}

public sealed record SetResourceRateCommand : ProjectCommand
{
    public required string Resource { get; init; }

    public CostRateTableId Table { get; init; }

    /// <summary>Null targets the base entry.</summary>
    public DateTime? From { get; init; }

    public string? Rate { get; init; }

    public string? OvertimeRate { get; init; }

    public decimal? CostPerUse { get; init; }
}

public sealed record RemoveResourceRateCommand : ProjectCommand
{
    public required string Resource { get; init; }

    public CostRateTableId Table { get; init; }

    public required DateTime From { get; init; }
}

public sealed record AssignCommand : ProjectCommand
{
    public required int Uid { get; init; }

    public required string Resource { get; init; }

    public decimal? Units { get; init; }

    public string? Work { get; init; }

    public decimal? Cost { get; init; }
}

public sealed record SetAssignmentCommand : ProjectCommand
{
    public required int Uid { get; init; }

    public required string Resource { get; init; }

    public decimal? Units { get; init; }

    public string? Work { get; init; }

    public WorkContour? Contour { get; init; }

    public string? Delay { get; init; }

    public CostRateTableId? RateTable { get; init; }

    public decimal? Cost { get; init; }
}

public sealed record UnassignCommand : ProjectCommand
{
    public required int Uid { get; init; }

    public required string Resource { get; init; }
}

public sealed record AddCalendarCommand : ProjectCommand
{
    public required string Name { get; init; }

    public string? BaseCalendar { get; init; }

    /// <summary>standard | 24h | night-shift; ignored when a base is given.</summary>
    public string? Preset { get; init; }
}

public sealed record RemoveCalendarCommand : ProjectCommand
{
    public required string Calendar { get; init; }
}

public sealed record CommandInterval(string Start, string End);

public sealed record SetCalendarDayCommand : ProjectCommand
{
    public required string Calendar { get; init; }

    public required DayOfWeek Day { get; init; }

    /// <summary>True = non-working; wins over intervals.</summary>
    public bool Off { get; init; }

    /// <summary>Working intervals; null with Off=false means inherit.</summary>
    public IReadOnlyList<CommandInterval>? Intervals { get; init; }
}

public sealed record SetCalendarBaseCommand : ProjectCommand
{
    public required string Calendar { get; init; }

    /// <summary>Null = standalone.</summary>
    public string? BaseCalendar { get; init; }
}

public sealed record CommandRecurrence
{
    public required string Kind { get; init; }

    public int Every { get; init; } = 1;

    public int Day { get; init; }

    public int Month { get; init; }

    public IReadOnlyList<DayOfWeek>? Days { get; init; }

    public Time.WeekOrdinal Ordinal { get; init; }

    public DayOfWeek Weekday { get; init; }
}

public sealed record AddCalendarExceptionCommand : ProjectCommand
{
    public required string Calendar { get; init; }

    public required string Name { get; init; }

    public required DateOnly From { get; init; }

    public DateOnly? To { get; init; }

    /// <summary>Working intervals; null or empty = the day is off.</summary>
    public IReadOnlyList<CommandInterval>? Intervals { get; init; }

    public CommandRecurrence? Recurrence { get; init; }

    public int? Times { get; init; }
}

public sealed record RemoveCalendarExceptionCommand : ProjectCommand
{
    public required string Calendar { get; init; }

    public required string Name { get; init; }
}

public sealed record AddWorkWeekCommand : ProjectCommand
{
    public required string Calendar { get; init; }

    public required string Name { get; init; }

    public required DateOnly From { get; init; }

    public required DateOnly To { get; init; }

    /// <summary>Per-day overrides; absent days inherit. Empty interval list = off.</summary>
    public IReadOnlyDictionary<DayOfWeek, IReadOnlyList<CommandInterval>>? Days { get; init; }
}

public sealed record RemoveWorkWeekCommand : ProjectCommand
{
    public required string Calendar { get; init; }

    public required string Name { get; init; }
}

public sealed record CommandIndicatorRule(string Op, string Value, string Icon);

public sealed record DefineCustomFieldCommand : ProjectCommand
{
    public required string Slot { get; init; }

    public string? Alias { get; init; }

    public string? Formula { get; init; }

    public IReadOnlyList<CommandIndicatorRule>? Indicators { get; init; }
}

public sealed record RemoveCustomFieldCommand : ProjectCommand
{
    public required string Field { get; init; }
}

public sealed record AddRecurringTaskCommand : ProjectCommand
{
    public required string Name { get; init; }

    public required string Duration { get; init; }

    public required CommandRecurrence Recurrence { get; init; }

    public required DateOnly From { get; init; }

    public DateOnly? Until { get; init; }

    public int? Times { get; init; }

    public int? ParentUid { get; init; }
}

public sealed record RescheduleCommand : ProjectCommand
{
    /// <summary>Cutoff; null uses the status date.</summary>
    public DateTime? After { get; init; }
}
