namespace Project27.Core;

/// <summary>Resource kinds, MS Project's three.</summary>
public enum ResourceType
{
    /// <summary>People/equipment: time-based, has units, work, and a calendar.</summary>
    Work,

    /// <summary>Consumables: quantity-based, costed per unit.</summary>
    Material,

    /// <summary>Pure expenses: an amount entered per assignment.</summary>
    Cost,
}

/// <summary>
/// Which corner of Work = Duration × Units the engine recalculates when another
/// corner is edited. See docs/spec/04-resources-costs.md.
/// </summary>
public enum TaskType
{
    FixedUnits,
    FixedDuration,
    FixedWork,
}

/// <summary>
/// Distribution of assignment work over time. Until usage views exist only the
/// average utilization matters: it stretches the assignment duration
/// (see <see cref="WorkContourExtensions.AverageUtilization"/>).
/// </summary>
public enum WorkContour
{
    Flat,
    BackLoaded,
    FrontLoaded,
    DoublePeak,
    EarlyPeak,
    LatePeak,
    Bell,
    Turtle,
}

/// <summary>When a cost is incurred within its assignment's span (time-phasing lands in phase 8/9).</summary>
public enum CostAccrual
{
    Start,
    Prorated,
    End,
}

public static class WorkContourExtensions
{
    /// <summary>
    /// Average utilization of assigned units over the assignment span; assignment
    /// duration = work / (units × average). Clean-room decile averages
    /// (deviations.md #14).
    /// </summary>
    public static decimal AverageUtilization(this WorkContour contour) => contour switch
    {
        WorkContour.Flat => 1.00m,
        WorkContour.BackLoaded => 0.60m,
        WorkContour.FrontLoaded => 0.60m,
        WorkContour.DoublePeak => 0.50m,
        WorkContour.EarlyPeak => 0.45m,
        WorkContour.LatePeak => 0.45m,
        WorkContour.Bell => 0.50m,
        WorkContour.Turtle => 0.70m,
        _ => throw new ArgumentOutOfRangeException(nameof(contour), contour, null),
    };
}
