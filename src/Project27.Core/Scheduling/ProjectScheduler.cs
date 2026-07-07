using Project27.Core.Time;

namespace Project27.Core.Scheduling;

/// <summary>
/// Full-recalculation CPM engine. Forward and backward passes are monotone worklist
/// fixpoints (dates only move later / earlier respectively), which handles summary
/// rollups, links to/from summaries, and manual-task islands without a strict
/// topological order. See docs/spec/02-scheduling.md.
/// </summary>
internal static class ProjectScheduler
{
    public static void Recalculate(Project project)
    {
        var tasks = project.Tasks;
        foreach (var task in tasks)
        {
            task.EarlyStart = task.EarlyFinish = task.LateStart = task.LateFinish = null;
            task.Start = task.Finish = null;
            task.TotalSlackMinutes = task.FreeSlackMinutes = null;
            task.IsCritical = false;
            task.Segments = [];
        }

        if (tasks.Count == 0)
        {
            if (project.ScheduleFrom == ScheduleFrom.ProjectStart)
            {
                project.FinishDate = project.StartDate;
            }

            return;
        }

        var guard = ComputeGuard(tasks);
        if (project.ScheduleFrom == ScheduleFrom.ProjectFinish && project.FinishDate is { } finishAnchor)
        {
            BackwardPass(project, tasks, finishAnchor, guard);
            var start = tasks.Where(t => t.IsActive && t.LateStart is not null).Min(t => t.LateStart) ?? finishAnchor;
            project.StartDate = start;
            ForwardPass(project, tasks, start, guard);
        }
        else
        {
            ForwardPass(project, tasks, project.StartDate, guard);
            var finish = tasks.Where(t => t.IsActive && t.EarlyFinish is not null).Max(t => t.EarlyFinish) ?? project.StartDate;
            project.FinishDate = finish;
            BackwardPass(project, tasks, finish, guard);
        }

        FinalizeDates(project, tasks);
        ComputeSlack(project, tasks);
    }

    private static long ComputeGuard(IReadOnlyList<ProjectTask> tasks)
    {
        long edges = tasks.Sum(t => (long)t.Successors.Count);
        var size = tasks.Count + edges + 1;
        return 1000 + (size * size);
    }

    // ---------------------------------------------------------------- forward

    private static void ForwardPass(Project project, IReadOnlyList<ProjectTask> tasks, DateTime anchor, long guard)
    {
        var pending = new Queue<ProjectTask>();
        var queued = new HashSet<ProjectTask>();
        foreach (var task in tasks)
        {
            if (!task.IsSummary)
            {
                Enqueue(task);
            }
        }

        while (pending.Count > 0)
        {
            if (--guard < 0)
            {
                throw new InvalidOperationException("Scheduling did not converge; this is an engine bug.");
            }

            var task = pending.Dequeue();
            queued.Remove(task);

            DateTime? start, finish;
            if (task.IsSummary)
            {
                (start, finish) = RollupEarly(project, task);
            }
            else
            {
                (start, finish) = ComputeEarly(project, task, anchor);
            }

            if (start == task.EarlyStart && finish == task.EarlyFinish)
            {
                continue;
            }

            task.EarlyStart = start;
            task.EarlyFinish = finish;

            if (task.Parent is { IsRoot: false } parent)
            {
                Enqueue(parent);
            }

            if (task.IsActive)
            {
                foreach (var dependency in task.SuccessorsList)
                {
                    foreach (var leaf in dependency.Successor.Leaves())
                    {
                        Enqueue(leaf);
                    }
                }
            }
        }

        void Enqueue(ProjectTask task)
        {
            if (queued.Add(task))
            {
                pending.Enqueue(task);
            }
        }
    }

    private static (DateTime? Start, DateTime? Finish) RollupEarly(Project project, ProjectTask summary)
    {
        DateTime? start = null, finish = null;
        foreach (var child in summary.ChildrenList)
        {
            if (!child.IsActive || child.EarlyStart is null)
            {
                continue;
            }

            start = start is null || child.EarlyStart < start ? child.EarlyStart : start;
            finish = finish is null || child.EarlyFinish > finish ? child.EarlyFinish : finish;
        }

        if (start is { } s && finish is { } f)
        {
            summary.DurationMinutes = project.Calendar.WorkBetween(s, f);
        }

        return (start, finish);
    }

    private static (DateTime? Start, DateTime? Finish) ComputeEarly(Project project, ProjectTask task, DateTime anchor)
    {
        var calendar = task.Calendar ?? project.Calendar;

        if (task.Mode == TaskMode.Manual)
        {
            var manualStart = task.ManualStart ?? calendar.NextWorkingTime(anchor);
            var manualFinish = task.ManualFinish
                ?? (task.DurationMinutes == 0 ? manualStart : calendar.AddWork(manualStart, task.DurationMinutes));
            if (task.ManualStart is not null && task.ManualFinish is not null)
            {
                task.DurationMinutes = calendar.WorkBetween(manualStart, manualFinish);
            }

            return (manualStart, manualFinish);
        }

        DateTime? startBound = anchor, finishBound = null, forcedStart = null, forcedFinish = null;

        for (var scope = task; scope is { IsRoot: false }; scope = scope.Parent)
        {
            foreach (var dependency in scope.PredecessorsList)
            {
                var predecessor = dependency.Predecessor;
                if (!predecessor.IsActive || predecessor.EarlyStart is null || predecessor.EarlyFinish is null)
                {
                    continue;
                }

                var basePoint = dependency.Type is DependencyType.FinishToStart or DependencyType.FinishToFinish
                    ? predecessor.EarlyFinish.Value
                    : predecessor.EarlyStart.Value;
                var point = ApplyLag(basePoint, dependency.Lag, calendar, predecessor.DurationMinutes, forward: true);
                if (dependency.Type is DependencyType.FinishToStart or DependencyType.StartToStart)
                {
                    startBound = Max(startBound, point);
                }
                else
                {
                    finishBound = Max(finishBound, point);
                }
            }
        }

        switch (task.Constraint)
        {
            case ConstraintType.StartNoEarlierThan:
                startBound = Max(startBound, task.ConstraintDate);
                break;
            case ConstraintType.FinishNoEarlierThan:
                finishBound = Max(finishBound, task.ConstraintDate);
                break;
            case ConstraintType.MustStartOn:
                forcedStart = task.ConstraintDate;
                break;
            case ConstraintType.MustFinishOn:
                forcedFinish = task.ConstraintDate;
                break;
            default:
                break;
        }

        if (task.DurationMinutes == 0 && !task.IsSplit)
        {
            var point = forcedStart ?? forcedFinish ?? Max(startBound, finishBound)!.Value;
            var snapped = SnapToWorkingPoint(calendar, point);
            return (snapped, snapped);
        }

        DateTime earlyStart;
        if (forcedStart is { } fs)
        {
            earlyStart = calendar.NextWorkingTime(fs);
        }
        else if (forcedFinish is { } ff)
        {
            earlyStart = ScheduleBackward(calendar, task, SnapToWorkingPoint(calendar, ff)).Start;
        }
        else
        {
            earlyStart = calendar.NextWorkingTime(startBound!.Value);
            if (finishBound is { } fb)
            {
                var fromFinish = ScheduleBackward(calendar, task, SnapToWorkingPoint(calendar, fb)).Start;
                earlyStart = Max(earlyStart, fromFinish)!.Value;
            }
        }

        var (finish, _) = ScheduleForward(calendar, task, earlyStart);
        return (earlyStart, finish);
    }

    // --------------------------------------------------------------- backward

    private static void BackwardPass(Project project, IReadOnlyList<ProjectTask> tasks, DateTime anchor, long guard)
    {
        var pending = new Queue<ProjectTask>();
        var queued = new HashSet<ProjectTask>();
        for (var i = tasks.Count - 1; i >= 0; i--)
        {
            if (!tasks[i].IsSummary)
            {
                Enqueue(tasks[i]);
            }
        }

        while (pending.Count > 0)
        {
            if (--guard < 0)
            {
                throw new InvalidOperationException("Scheduling did not converge; this is an engine bug.");
            }

            var task = pending.Dequeue();
            queued.Remove(task);

            DateTime? lateStart, lateFinish;
            if (task.IsSummary)
            {
                (lateStart, lateFinish) = RollupLate(task);
            }
            else
            {
                (lateStart, lateFinish) = ComputeLate(project, task, anchor);
            }

            if (lateStart == task.LateStart && lateFinish == task.LateFinish)
            {
                continue;
            }

            task.LateStart = lateStart;
            task.LateFinish = lateFinish;

            if (task.Parent is { IsRoot: false } parent)
            {
                Enqueue(parent);
            }

            for (var scope = task; scope is { IsRoot: false }; scope = scope.Parent)
            {
                foreach (var dependency in scope.PredecessorsList)
                {
                    if (dependency.Predecessor.IsActive || dependency.Predecessor.IsSummary)
                    {
                        foreach (var leaf in dependency.Predecessor.Leaves())
                        {
                            Enqueue(leaf);
                        }
                    }
                }
            }
        }

        void Enqueue(ProjectTask task)
        {
            if (queued.Add(task))
            {
                pending.Enqueue(task);
            }
        }
    }

    private static (DateTime? Start, DateTime? Finish) RollupLate(ProjectTask summary)
    {
        DateTime? start = null, finish = null;
        foreach (var child in summary.ChildrenList)
        {
            if (!child.IsActive || child.LateStart is null)
            {
                continue;
            }

            start = start is null || child.LateStart < start ? child.LateStart : start;
            finish = finish is null || child.LateFinish > finish ? child.LateFinish : finish;
        }

        return (start, finish);
    }

    private static (DateTime? Start, DateTime? Finish) ComputeLate(Project project, ProjectTask task, DateTime anchor)
    {
        var calendar = task.Calendar ?? project.Calendar;
        DateTime? finishBound = anchor, startBound = null, forcedStart = null, forcedFinish = null;

        for (var scope = task; scope is { IsRoot: false }; scope = scope.Parent)
        {
            foreach (var dependency in scope.SuccessorsList)
            {
                var successor = dependency.Successor;
                if (!successor.IsActive)
                {
                    continue;
                }

                // A link into a summary was inherited by each of its leaves on the
                // forward pass, so its inverse bound is the min over those leaves.
                var (succLateStart, succLateFinish) = MinLeafLate(successor);
                var basePoint = dependency.Type is DependencyType.FinishToStart or DependencyType.StartToStart
                    ? succLateStart
                    : succLateFinish;
                if (basePoint is null)
                {
                    continue;
                }

                var successorCalendar = successor.Calendar ?? project.Calendar;
                var point = ApplyLag(basePoint.Value, dependency.Lag, successorCalendar, task.DurationMinutes, forward: false);
                if (dependency.Type is DependencyType.FinishToStart or DependencyType.FinishToFinish)
                {
                    finishBound = Min(finishBound, point);
                }
                else
                {
                    startBound = Min(startBound, point);
                }
            }
        }

        switch (task.Constraint)
        {
            case ConstraintType.StartNoLaterThan:
                startBound = Min(startBound, task.ConstraintDate);
                break;
            case ConstraintType.FinishNoLaterThan:
                finishBound = Min(finishBound, task.ConstraintDate);
                break;
            case ConstraintType.MustStartOn:
                forcedStart = task.ConstraintDate;
                break;
            case ConstraintType.MustFinishOn:
                forcedFinish = task.ConstraintDate;
                break;
            default:
                break;
        }

        if (task.Deadline is { } deadline)
        {
            finishBound = Min(finishBound, deadline);
        }

        if (task.DurationMinutes == 0 && !task.IsSplit)
        {
            var point = forcedStart ?? forcedFinish ?? Min(finishBound, startBound)!.Value;
            var snapped = SnapToWorkingPoint(calendar, point, backward: true);
            return (snapped, snapped);
        }

        DateTime lateFinish;
        if (forcedFinish is { } ff)
        {
            lateFinish = SnapToWorkingPoint(calendar, ff, backward: true);
        }
        else if (forcedStart is { } fs)
        {
            lateFinish = ScheduleForward(calendar, task, calendar.NextWorkingTime(fs)).Finish;
        }
        else
        {
            lateFinish = SnapToWorkingPoint(calendar, finishBound!.Value, backward: true);
            if (startBound is { } sb)
            {
                var fromStart = ScheduleForward(calendar, task, SnapStartLimit(calendar, sb)).Finish;
                lateFinish = Min(lateFinish, fromStart)!.Value;
            }
        }

        var (lateStart, _) = ScheduleBackward(calendar, task, lateFinish);
        return (lateStart, lateFinish);
    }

    private static (DateTime? Start, DateTime? Finish) MinLeafLate(ProjectTask task)
    {
        if (!task.IsSummary)
        {
            return (task.LateStart, task.LateFinish);
        }

        DateTime? start = null, finish = null;
        foreach (var leaf in task.Leaves())
        {
            if (!leaf.IsActive || leaf.LateStart is null)
            {
                continue;
            }

            start = Min(start, leaf.LateStart);
            finish = Min(finish, leaf.LateFinish);
        }

        return (start, finish);
    }

    // --------------------------------------------------------------- finalize

    private static void FinalizeDates(Project project, IReadOnlyList<ProjectTask> tasks)
    {
        // Children precede parents when walking the pre-order list backwards.
        for (var i = tasks.Count - 1; i >= 0; i--)
        {
            var task = tasks[i];
            var calendar = task.Calendar ?? project.Calendar;
            if (task.IsSummary)
            {
                DateTime? start = null, finish = null;
                foreach (var child in task.ChildrenList)
                {
                    if (!child.IsActive || child.Start is null)
                    {
                        continue;
                    }

                    start = start is null || child.Start < start ? child.Start : start;
                    finish = finish is null || child.Finish > finish ? child.Finish : finish;
                }

                task.Start = start;
                task.Finish = finish;
                if (start is { } s && finish is { } f)
                {
                    task.DurationMinutes = project.Calendar.WorkBetween(s, f);
                    task.Segments = [new TaskSegment(s, f)];
                }

                continue;
            }

            if (task.Mode == TaskMode.Manual || task.Constraint != ConstraintType.AsLateAsPossible)
            {
                task.Start = task.EarlyStart;
                task.Finish = task.EarlyFinish;
                if (task.EarlyStart is { } es)
                {
                    task.Segments = task.Mode == TaskMode.Manual
                        ? [new TaskSegment(es, task.EarlyFinish!.Value)]
                        : ScheduleForward(calendar, task, es).Segments;
                }
            }
            else if (task.LateFinish is { } lf)
            {
                var (start, segments) = ScheduleBackward(calendar, task, lf);
                task.Start = start;
                task.Finish = lf;
                task.Segments = segments;
            }
        }

        if (project.ScheduleFrom == ScheduleFrom.ProjectStart)
        {
            project.FinishDate = tasks.Where(t => t.IsActive && t.Finish is not null).Max(t => t.Finish) ?? project.StartDate;
        }
        else
        {
            project.StartDate = tasks.Where(t => t.IsActive && t.Start is not null).Min(t => t.Start) ?? project.StartDate;
        }
    }

    private static void ComputeSlack(Project project, IReadOnlyList<ProjectTask> tasks)
    {
        var projectFinish = project.FinishDate ?? project.StartDate;
        foreach (var task in tasks)
        {
            if (task.Start is not { } start || task.LateStart is not { } lateStart)
            {
                continue;
            }

            var calendar = task.Calendar ?? project.Calendar;
            var startSlack = calendar.WorkBetween(start, lateStart);
            var finishSlack = calendar.WorkBetween(task.Finish!.Value, task.LateFinish!.Value);
            var totalSlack = Math.Min(startSlack, finishSlack);
            task.TotalSlackMinutes = totalSlack;
            task.IsCritical = task.IsActive && totalSlack <= project.CriticalSlackThresholdMinutes;

            decimal? freeSlack = null;
            foreach (var dependency in task.SuccessorsList)
            {
                var successor = dependency.Successor;
                if (!successor.IsActive || successor.Start is null)
                {
                    continue;
                }

                var successorCalendar = successor.Calendar ?? project.Calendar;
                var basePoint = dependency.Type is DependencyType.FinishToStart or DependencyType.FinishToFinish
                    ? task.Finish.Value
                    : start;
                var imposed = ApplyLag(basePoint, dependency.Lag, successorCalendar, task.DurationMinutes, forward: true);
                var target = dependency.Type is DependencyType.FinishToStart or DependencyType.StartToStart
                    ? successor.Start.Value
                    : successor.Finish!.Value;
                var slip = successorCalendar.WorkBetween(imposed, target);
                freeSlack = freeSlack is { } current ? Math.Min(current, slip) : slip;
            }

            task.FreeSlackMinutes = freeSlack ?? calendar.WorkBetween(task.Finish.Value, projectFinish);
        }
    }

    // ---------------------------------------------------------------- helpers

    private static (DateTime Finish, List<TaskSegment> Segments) ScheduleForward(WorkCalendar calendar, ProjectTask task, DateTime start)
    {
        var parts = task.SplitParts;
        var segments = new List<TaskSegment>(parts.Count);
        var cursor = start;
        foreach (var (work, gap) in parts)
        {
            var segmentStart = segments.Count == 0 ? cursor : calendar.NextWorkingTime(cursor);
            var segmentFinish = work == 0 ? segmentStart : calendar.AddWork(segmentStart, work);
            segments.Add(new TaskSegment(segmentStart, segmentFinish));
            cursor = gap > 0 ? calendar.AddWork(segmentFinish, gap) : segmentFinish;
        }

        return (segments[^1].Finish, segments);
    }

    private static (DateTime Start, List<TaskSegment> Segments) ScheduleBackward(WorkCalendar calendar, ProjectTask task, DateTime finish)
    {
        var parts = task.SplitParts;
        var segments = new TaskSegment[parts.Count];
        var cursor = finish;
        for (var i = parts.Count - 1; i >= 0; i--)
        {
            var segmentFinish = cursor;
            var segmentStart = parts[i].WorkMinutes == 0 ? segmentFinish : calendar.AddWork(segmentFinish, -parts[i].WorkMinutes);
            segments[i] = new TaskSegment(segmentStart, segmentFinish);
            if (i > 0)
            {
                var gapBefore = parts[i - 1].GapMinutes;
                cursor = gapBefore > 0 ? calendar.AddWork(segmentStart, -gapBefore) : segmentStart;
            }
        }

        return (segments[0].Start, [.. segments]);
    }

    private static DateTime ApplyLag(DateTime point, Lag lag, WorkCalendar successorCalendar, decimal predecessorDurationMinutes, bool forward)
    {
        if (lag.IsZero)
        {
            return point;
        }

        var minutes = lag.Kind == LagKind.Percent
            ? predecessorDurationMinutes * lag.Value / 100m
            : lag.Value;
        if (!forward)
        {
            minutes = -minutes;
        }

        return lag.Kind == LagKind.Elapsed
            ? point.AddTicks((long)(minutes * TimeSpan.TicksPerMinute))
            : successorCalendar.AddWork(point, minutes);
    }

    /// <summary>
    /// Snaps a point to working time: kept as-is when inside working time or exactly at
    /// an interval end (a valid finish); otherwise moved to the next (or, backward, the
    /// previous) working instant.
    /// </summary>
    private static DateTime SnapToWorkingPoint(WorkCalendar calendar, DateTime point, bool backward = false)
    {
        if (calendar.PreviousWorkingTime(point) == point)
        {
            return point;
        }

        return backward ? calendar.PreviousWorkingTime(point) : calendar.NextWorkingTime(point);
    }

    /// <summary>Largest usable start at or before the given start-no-later bound.</summary>
    private static DateTime SnapStartLimit(WorkCalendar calendar, DateTime bound)
        => calendar.IsWorkingTime(bound) ? bound : calendar.PreviousWorkingTime(bound);

    private static DateTime? Max(DateTime? a, DateTime? b) => a is null ? b : b is null ? a : (a > b ? a : b);

    private static DateTime? Min(DateTime? a, DateTime? b) => a is null ? b : b is null ? a : (a < b ? a : b);
}
