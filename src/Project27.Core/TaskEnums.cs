namespace Project27.Core;

/// <summary>How a task's dates are determined.</summary>
public enum TaskMode
{
    /// <summary>Scheduled by the engine from dependencies, constraints, and calendars.</summary>
    Auto,

    /// <summary>Dates are user-entered; the engine never moves them.</summary>
    Manual,
}

/// <summary>MS Project's eight scheduling constraints.</summary>
public enum ConstraintType
{
    AsSoonAsPossible,
    AsLateAsPossible,
    StartNoEarlierThan,
    StartNoLaterThan,
    FinishNoEarlierThan,
    FinishNoLaterThan,
    MustStartOn,
    MustFinishOn,
}

/// <summary>Dependency link types (predecessor → successor).</summary>
public enum DependencyType
{
    FinishToStart,
    StartToStart,
    FinishToFinish,
    StartToFinish,
}

/// <summary>Whether the project is anchored at its start or its finish date.</summary>
public enum ScheduleFrom
{
    ProjectStart,
    ProjectFinish,
}
