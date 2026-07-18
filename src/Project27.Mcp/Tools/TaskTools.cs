using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Project27.Core;
using Project27.Core.Commands;
using Project27.Mcp.Session;

namespace Project27.Mcp.Tools;

[McpServerToolType]
public sealed class TaskTools(IProjectSession session)
{
    [McpServerTool(Name = "task_write"), Description(
        "Creates, edits, removes, or restructures tasks. `op` selects the shape of the other parameters:\n" +
        "- add: name (required), duration, parentUid, at, milestone\n" +
        "- set: uid (required), any of name/duration/mode/active/milestone/priority/spaceAfter/deadline/constraint/" +
        "constraintDate/calendar/wbs/manualStart/manualFinish/type/effortDriven/fixedCost/fixedCostAccrual/" +
        "ignoreResourceCalendars/percentComplete/actualStart/actualFinish/remainingDuration/customValues, and the " +
        "matching clear* flag (e.g. clearDeadline) to blank a field instead of leaving it unchanged\n" +
        "- remove: uid\n" +
        "- move: uid, parentUid (null = top level), at (0-based position)\n" +
        "- indent / outdent: uid\n" +
        "- split: uid, splitAt (engine duration offset from task start), gap (engine duration)\n" +
        "- unsplit: uid\n" +
        "- addRecurring: name, duration, recurrence (required), from (required), until, times, parentUid\n" +
        "Durations use engine syntax (\"3d\", \"4eh\"). Call get_project/recalculate is automatic — no separate step needed.")]
    public async Task<string> TaskWrite(
        [Description("add|set|remove|move|indent|outdent|split|unsplit|addRecurring")] string op,
        int? uid = null,
        string? name = null,
        string? duration = null,
        int? parentUid = null,
        bool clearParentUid = false,
        int? at = null,
        bool? milestone = null,
        TaskMode? mode = null,
        bool? active = null,
        int? priority = null,
        int? spaceAfter = null,
        DateTime? deadline = null,
        bool clearDeadline = false,
        ConstraintType? constraint = null,
        DateTime? constraintDate = null,
        string? calendar = null,
        bool clearCalendar = false,
        string? wbs = null,
        bool clearWbs = false,
        DateTime? manualStart = null,
        bool clearManualStart = false,
        DateTime? manualFinish = null,
        bool clearManualFinish = false,
        TaskType? type = null,
        bool? effortDriven = null,
        decimal? fixedCost = null,
        CostAccrual? fixedCostAccrual = null,
        bool? ignoreResourceCalendars = null,
        int? percentComplete = null,
        DateTime? actualStart = null,
        bool clearActualStart = false,
        DateTime? actualFinish = null,
        bool clearActualFinish = false,
        string? remainingDuration = null,
        Dictionary<string, string?>? customValues = null,
        [Description("split: engine duration offset from the task's start, e.g. \"2d\".")] string? splitAt = null,
        string? gap = null,
        CommandRecurrence? recurrence = null,
        DateOnly? from = null,
        DateOnly? until = null,
        int? times = null,
        CancellationToken cancellationToken = default)
    {
        ProjectCommand command = op.Trim().ToUpperInvariant() switch
        {
            "ADD" => new AddTaskCommand
            {
                Name = name ?? throw new ArgumentException("name is required for op=add"),
                Duration = duration,
                ParentUid = parentUid,
                At = at,
                Milestone = milestone ?? false,
            },
            "SET" => new SetTaskCommand
            {
                Uid = uid ?? throw new ArgumentException("uid is required for op=set"),
                Name = name,
                Duration = duration,
                Mode = mode,
                Active = active,
                Milestone = milestone,
                Priority = priority,
                SpaceAfter = spaceAfter,
                Deadline = deadline,
                ClearDeadline = clearDeadline,
                Constraint = constraint,
                ConstraintDate = constraintDate,
                Calendar = calendar,
                ClearCalendar = clearCalendar,
                Wbs = wbs,
                ClearWbs = clearWbs,
                ManualStart = manualStart,
                ClearManualStart = clearManualStart,
                ManualFinish = manualFinish,
                ClearManualFinish = clearManualFinish,
                Type = type,
                EffortDriven = effortDriven,
                FixedCost = fixedCost,
                FixedCostAccrual = fixedCostAccrual,
                IgnoreResourceCalendars = ignoreResourceCalendars,
                PercentComplete = percentComplete,
                ActualStart = actualStart,
                ClearActualStart = clearActualStart,
                ActualFinish = actualFinish,
                ClearActualFinish = clearActualFinish,
                RemainingDuration = remainingDuration,
                CustomValues = customValues,
            },
            "REMOVE" => new RemoveTaskCommand { Uid = uid ?? throw new ArgumentException("uid is required for op=remove") },
            "MOVE" => new MoveTaskCommand
            {
                Uid = uid ?? throw new ArgumentException("uid is required for op=move"),
                ParentUid = clearParentUid ? null : parentUid,
                At = at ?? throw new ArgumentException("at is required for op=move"),
            },
            "INDENT" => new IndentTaskCommand { Uid = uid ?? throw new ArgumentException("uid is required for op=indent") },
            "OUTDENT" => new OutdentTaskCommand { Uid = uid ?? throw new ArgumentException("uid is required for op=outdent") },
            "SPLIT" => new SplitTaskCommand
            {
                Uid = uid ?? throw new ArgumentException("uid is required for op=split"),
                At = splitAt ?? throw new ArgumentException("splitAt (engine duration offset) is required for op=split"),
                Gap = gap ?? throw new ArgumentException("gap (engine duration) is required for op=split"),
            },
            "UNSPLIT" => new UnsplitTaskCommand { Uid = uid ?? throw new ArgumentException("uid is required for op=unsplit") },
            "ADDRECURRING" => new AddRecurringTaskCommand
            {
                Name = name ?? throw new ArgumentException("name is required for op=addRecurring"),
                Duration = duration ?? throw new ArgumentException("duration is required for op=addRecurring"),
                Recurrence = recurrence ?? throw new ArgumentException("recurrence is required for op=addRecurring"),
                From = from ?? throw new ArgumentException("from is required for op=addRecurring"),
                Until = until,
                Times = times,
                ParentUid = parentUid,
            },
            _ => throw new ArgumentException($"Unknown op '{op}'."),
        };
        var result = await session.ApplyAsync([command], cancellationToken);
        return JsonSerializer.Serialize(result, ReadTools.JsonOptions);
    }
}
