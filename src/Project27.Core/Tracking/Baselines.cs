namespace Project27.Core;

/// <summary>A task's captured plan in one baseline slot (0–10).</summary>
public readonly record struct TaskBaseline(
    DateTime? Start,
    DateTime? Finish,
    decimal DurationMinutes,
    decimal WorkMinutes,
    decimal Cost);

/// <summary>An assignment's captured work and cost in one baseline slot.</summary>
public readonly record struct AssignmentBaseline(decimal WorkMinutes, decimal Cost);
