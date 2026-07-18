using Project27.Core.Time;

namespace Project27.Core;

/// <summary>Earned-value figures for a task or project at the status date.</summary>
public sealed record EarnedValueData(
    decimal Bac,
    decimal Bcws,
    decimal Bcwp,
    decimal Acwp,
    decimal Sv,
    decimal Cv,
    decimal? Spi,
    decimal? Cpi,
    decimal Eac,
    decimal Vac,
    decimal? Tcpi)
{
    public static EarnedValueData Zero { get; } = new(0, 0, 0, 0, 0, 0, null, null, 0, 0, null);
}

/// <summary>
/// EVM against baseline slot 0 (docs/spec/08-tracking-evm.md). Schedule-dependent:
/// recalculate before asking. BCWS places each assignment's baseline cost by its
/// resource's accrual — prorated cost spreads linearly over the baseline span's
/// working time, start/end-accrued cost lands as a lump (deviation #19, closed).
/// ACWP is the task's actual cost: explicit assignment actuals where entered, else
/// derived from percent complete (deviation #20, closed).
/// </summary>
public static class EarnedValue
{
    public static EarnedValueData ForProject(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Aggregate(project, project.Tasks.Where(t => t.OutlineLevel == 0 && t.IsActive), slot: 0);
    }

    public static EarnedValueData ForTask(ProjectTask task, int slot = 0)
    {
        ArgumentNullException.ThrowIfNull(task);
        return task.IsSummary
            ? Aggregate(task.Project, task.Children.Where(c => c.IsActive), slot)
            : Leaf(task, slot);
    }

    private static EarnedValueData Aggregate(Project project, IEnumerable<ProjectTask> tasks, int slot)
    {
        decimal bac = 0, bcws = 0, bcwp = 0, acwp = 0, currentCost = 0;
        foreach (var task in tasks)
        {
            var data = ForTask(task, slot);
            bac += data.Bac;
            bcws += data.Bcws;
            bcwp += data.Bcwp;
            acwp += data.Acwp;
            currentCost += task.Cost;
        }

        return Derive(bac, bcws, bcwp, acwp, currentCost);
    }

    private static EarnedValueData Leaf(ProjectTask task, int slot)
    {
        var baseline = task.Baseline(slot);
        if (baseline is not { } captured)
        {
            return EarnedValueData.Zero;
        }

        var project = task.Project;
        var statusDate = project.StatusDate ?? project.FinishDate ?? project.StartDate;
        var bac = captured.Cost;
        var bcws = Bcws(task, captured, slot, PlannedFraction(task, captured, statusDate));
        var bcwp = bac * task.PercentComplete / 100m;
        var acwp = task.ActualCost;
        return Derive(bac, bcws, bcwp, acwp, task.Cost);
    }

    /// <summary>
    /// Baseline cost due by the status date: each baselined assignment's cost placed
    /// by its resource's accrual; the remainder (fixed cost plus assignments without
    /// baselines) by the task's fixed-cost accrual (deviation #19).
    /// </summary>
    private static decimal Bcws(ProjectTask task, TaskBaseline captured, int slot, decimal fraction)
    {
        decimal due = 0m, assigned = 0m;
        foreach (var assignment in task.AssignmentsList)
        {
            if (assignment.Baseline(slot) is not { } baseline)
            {
                continue;
            }

            assigned += baseline.Cost;
            var accrual = assignment.Resource.Type == ResourceType.Cost
                ? CostAccrual.Prorated
                : assignment.Resource.Accrual;
            due += Accrue(baseline.Cost, accrual, fraction);
        }

        return due + Accrue(captured.Cost - assigned, task.FixedCostAccrual, fraction);

        static decimal Accrue(decimal cost, CostAccrual accrual, decimal fraction) => accrual switch
        {
            CostAccrual.Start => fraction > 0m ? cost : 0m,
            CostAccrual.End => fraction >= 1m ? cost : 0m,
            _ => cost * fraction,
        };
    }

    /// <summary>Share of the baseline span's working time that lies at or before the status date.</summary>
    private static decimal PlannedFraction(ProjectTask task, TaskBaseline baseline, DateTime statusDate)
    {
        if (baseline.Start is not { } start)
        {
            return 0m;
        }

        if (statusDate <= start)
        {
            return 0m;
        }

        if (baseline.Finish is not { } finish || statusDate >= finish || baseline.DurationMinutes <= 0)
        {
            return 1m;
        }

        var calendar = task.Calendar ?? task.Project.Calendar;
        var elapsed = calendar.WorkBetween(start, statusDate);
        return Math.Clamp(elapsed / baseline.DurationMinutes, 0m, 1m);
    }

    private static EarnedValueData Derive(decimal bac, decimal bcws, decimal bcwp, decimal acwp, decimal currentCost)
    {
        var spi = bcws == 0 ? (decimal?)null : bcwp / bcws;
        var cpi = acwp == 0 ? (decimal?)null : bcwp / acwp;
        var eac = cpi is > 0 ? bac / cpi.Value : currentCost;
        var tcpiDenominator = bac - acwp;
        return new EarnedValueData(
            Bac: bac,
            Bcws: bcws,
            Bcwp: bcwp,
            Acwp: acwp,
            Sv: bcwp - bcws,
            Cv: bcwp - acwp,
            Spi: spi,
            Cpi: cpi,
            Eac: eac,
            Vac: bac - eac,
            Tcpi: tcpiDenominator > 0 ? (bac - bcwp) / tcpiDenominator : null);
    }
}
