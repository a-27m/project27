namespace Project27.Core;

/// <summary>
/// Cosmetic, non-scheduling display attributes for a task row. Never read by
/// <see cref="Project.Recalculate"/>, the CPM pass, WBS numbering, or MSPDI/CSV export —
/// purely presentational, extensible for future fields (color, etc.) alongside SpaceAfter.
/// </summary>
public sealed class TaskFormatting
{
    private int _spaceAfter;

    /// <summary>Extra blank rows of visual breathing room shown after this task. 0-20.</summary>
    public int SpaceAfter
    {
        get => _spaceAfter;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 20);
            _spaceAfter = value;
        }
    }

    /// <summary>True once every field is back at its default; callers collapse this to null.</summary>
    public bool IsDefault => _spaceAfter == 0;
}
