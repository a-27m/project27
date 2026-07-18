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
/// Distribution of assignment work over time, defined by per-decile utilisation
/// tables (<see cref="WorkContourExtensions.Deciles"/>). The average stretches the
/// assignment duration; the deciles shape usage, cost proration, and leveling.
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
    // Clean-room per-decile utilisation patterns (percent of assigned units per tenth
    // of the assignment's working span). Single source of truth: scheduling, usage
    // views, cost proration, and leveling all derive from these tables, so the
    // scheduled span and the observable distribution cannot disagree (deviations.md #14).
    private static readonly int[][] DecileTables =
    [
        /* Flat        */ [100, 100, 100, 100, 100, 100, 100, 100, 100, 100],
        /* BackLoaded  */ [10, 15, 25, 50, 50, 75, 75, 100, 100, 100],
        /* FrontLoaded */ [100, 100, 100, 75, 75, 50, 50, 25, 15, 10],
        /* DoublePeak  */ [25, 50, 100, 50, 25, 25, 50, 100, 50, 25],
        /* EarlyPeak   */ [25, 50, 100, 100, 50, 50, 25, 25, 15, 10],
        /* LatePeak    */ [10, 15, 25, 25, 50, 50, 100, 100, 50, 25],
        /* Bell        */ [10, 20, 40, 80, 100, 100, 80, 40, 20, 10],
        /* Turtle      */ [25, 50, 75, 100, 100, 100, 100, 75, 50, 25],
    ];

    /// <summary>Per-decile utilisation (percent of assigned units) over the assignment's working span.</summary>
    public static IReadOnlyList<int> Deciles(this WorkContour contour)
        => (uint)contour < (uint)DecileTables.Length
            ? DecileTables[(int)contour]
            : throw new ArgumentOutOfRangeException(nameof(contour), contour, null);

    /// <summary>
    /// Average utilization of assigned units over the assignment span; assignment
    /// duration = work / (units × average). The decile distribution is defined over
    /// working-time tenths of that span, so this closed form is exactly the finish a
    /// bucket-by-bucket decile walk reaches (deviations.md #14).
    /// </summary>
    public static decimal AverageUtilization(this WorkContour contour)
        => Deciles(contour).Sum() / 1000m;
}
