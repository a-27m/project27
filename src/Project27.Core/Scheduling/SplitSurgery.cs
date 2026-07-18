using Project27.Core.Time;

namespace Project27.Core.Scheduling;

/// <summary>
/// Rewrites a scheduled leaf task's split parts so work at or after a point resumes
/// later, never moving completed work. Shared by
/// <see cref="Project.RescheduleUncompletedWork"/> (deviation #23) and split-based
/// leveling of started tasks (deviation #29). The caller recalculates.
/// </summary>
internal static class SplitSurgery
{
    /// <summary>
    /// Pushes the task's work at or after <paramref name="from"/> — but never work
    /// already completed — so it resumes at <paramref name="resumeAt"/>. Later
    /// segments keep their relative structure and shift as a block. Returns false
    /// when nothing is movable or the work already resumes at or past the target.
    /// </summary>
    public static bool PushWork(ProjectTask task, WorkCalendar calendar, DateTime from, DateTime resumeAt)
    {
        var parts = task.SplitParts;
        var segments = task.Segments;
        if (task.Start is null || task.DurationMinutes <= 0 || segments.Count != parts.Count)
        {
            return false;
        }

        var offset = Math.Max(WorkedMinutesAt(segments, calendar, from), task.CompletedMinutes);
        if (offset <= 0 || offset >= task.DurationMinutes)
        {
            return false; // nothing before the point, or nothing after it to move
        }

        var index = -1;
        var offsetInPart = 0m;
        var consumed = 0m;
        for (var i = 0; i < parts.Count; i++)
        {
            if (offset < consumed + parts[i].WorkMinutes)
            {
                index = i;
                offsetInPart = offset - consumed;
                break;
            }

            consumed += parts[i].WorkMinutes;
        }

        if (index < 0)
        {
            return false;
        }

        var rewritten = new List<(decimal WorkMinutes, decimal GapMinutes)>(parts);
        if (offsetInPart == 0m)
        {
            // The point falls exactly on a split boundary: widen the gap before this part.
            if (segments[index].Start >= resumeAt)
            {
                return false;
            }

            var gap = calendar.WorkBetween(segments[index - 1].Finish, resumeAt);
            if (gap <= 0)
            {
                return false;
            }

            rewritten[index - 1] = (rewritten[index - 1].WorkMinutes, gap);
        }
        else
        {
            var splitPoint = calendar.AddWork(segments[index].Start, offsetInPart);
            if (splitPoint >= resumeAt)
            {
                return false;
            }

            var gap = calendar.WorkBetween(splitPoint, resumeAt);
            if (gap <= 0)
            {
                return false;
            }

            var (work, gapAfter) = rewritten[index];
            rewritten[index] = (offsetInPart, gap);
            rewritten.Insert(index + 1, (work - offsetInPart, gapAfter));
        }

        task.RestoreSplitParts(rewritten);
        return true;
    }

    /// <summary>Scheduled working minutes that lie before <paramref name="from"/>, across the task's segments.</summary>
    private static decimal WorkedMinutesAt(IReadOnlyList<TaskSegment> segments, WorkCalendar calendar, DateTime from)
    {
        var minutes = 0m;
        foreach (var segment in segments)
        {
            if (segment.Start >= from)
            {
                break;
            }

            minutes += calendar.WorkBetween(segment.Start, segment.Finish < from ? segment.Finish : from);
        }

        return minutes;
    }
}
