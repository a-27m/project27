using Project27.Core.Time;

namespace Project27.Core;

/// <summary>Project-level current/baseline/actual/remaining figures for the Overview accordion.</summary>
public sealed record DateStat(DateTime? Current, DateTime? Baseline, DateTime? Actual, decimal? VarianceMinutes);

public sealed record AmountStat(decimal Current, decimal? Baseline, decimal? Actual, decimal? Remaining);

public sealed record ProjectStatsData(
    DateStat Start,
    DateStat Finish,
    AmountStat Duration,
    AmountStat Work,
    AmountStat Cost,
    int PercentCompleteByDuration,
    int PercentCompleteByWork);

/// <summary>
/// Aggregates top-level active tasks into the Overview stats matrix (docs handoff: project
/// scope Overview accordion). Baseline/Actual reuse the same slot-0 baseline and
/// percent-complete-derived "actual" convention as <see cref="EarnedValue"/> (deviation #20:
/// no independent actual-cost/work tracking yet).
/// </summary>
public static class ProjectStats
{
    public static ProjectStatsData For(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var topLevel = project.Tasks.Where(t => t.OutlineLevel == 0 && t.IsActive).ToList();
        var baselined = topLevel.Where(t => t.Baseline() is not null).ToList();

        DateTime? baselineStart = baselined.Count == 0 ? null : baselined.Min(t => t.Baseline()!.Value.Start);
        DateTime? baselineFinish = baselined.Count == 0 ? null : baselined.Max(t => t.Baseline()!.Value.Finish);
        DateTime? actualStart = topLevel.Select(t => t.ActualStart).Where(d => d is not null).DefaultIfEmpty(null).Min();
        var allLeavesComplete = topLevel.Count > 0 && topLevel.All(t => t.PercentComplete == 100);
        DateTime? actualFinish = allLeavesComplete
            ? topLevel.Select(t => t.ActualFinish).Where(d => d is not null).DefaultIfEmpty(null).Max()
            : null;

        var start = new DateStat(
            project.StartDate,
            baselineStart,
            actualStart,
            baselineStart is { } bstart ? (decimal)(project.StartDate - bstart).TotalMinutes : null);
        var finish = new DateStat(
            project.FinishDate,
            baselineFinish,
            actualFinish,
            project.FinishDate is { } f && baselineFinish is { } bf ? (decimal)(f - bf).TotalMinutes : null);

        var calendar = project.Calendar;
        var durationCurrent = project.FinishDate is { } finishDate
            ? calendar.WorkBetween(project.StartDate, finishDate)
            : 0m;
        decimal? durationBaseline = baselineStart is { } bs && baselineFinish is { } bfin
            ? calendar.WorkBetween(bs, bfin)
            : null;
        var durationRemaining = topLevel.Sum(t => t.RemainingMinutes);

        var workCurrent = project.TotalWorkMinutes;
        decimal? workBaseline = baselined.Count == 0 ? null : baselined.Sum(t => t.Baseline()!.Value.WorkMinutes);
        var workActual = topLevel.Sum(t => t.WorkMinutes * t.PercentComplete / 100m);
        var workRemaining = workCurrent - workActual;

        var costCurrent = project.TotalCost;
        var evm = EarnedValue.ForProject(project);
        decimal? costBaseline = baselined.Count == 0 ? null : evm.Bac;
        var costActual = evm.Acwp;
        var costRemaining = costCurrent - costActual;

        var duration = new AmountStat(durationCurrent, durationBaseline, null, durationRemaining);
        var work = new AmountStat(workCurrent, workBaseline, workActual, workRemaining);
        var cost = new AmountStat(costCurrent, costBaseline, costActual, costRemaining);

        var percentByDuration = ProjectPercentByDuration(topLevel);
        var percentByWork = workCurrent == 0 ? 0 : (int)Math.Round(workActual / workCurrent * 100m);

        return new ProjectStatsData(start, finish, duration, work, cost, percentByDuration, percentByWork);
    }

    private static int ProjectPercentByDuration(List<ProjectTask> topLevel)
    {
        decimal total = 0m, completed = 0m;
        foreach (var task in topLevel)
        {
            total += task.DurationMinutes;
            completed += task.CompletedMinutes;
        }

        return total == 0 ? 0 : (int)Math.Round(completed / total * 100m);
    }
}
