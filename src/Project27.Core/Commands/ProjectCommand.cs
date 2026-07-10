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
}
