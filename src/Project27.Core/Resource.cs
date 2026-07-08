using Project27.Core.Time;

namespace Project27.Core;

/// <summary>
/// A work, material, or cost resource. Names are unique per project
/// (case-insensitive; deviations.md #11).
/// </summary>
public sealed class Resource
{
    private readonly CostRateTable[] _rateTables = [new(), new(), new(), new(), new()];
    private readonly List<Assignment> _assignments = [];
    private string _name;
    private decimal _maxUnits = 1m;
    private WorkCalendar? _calendar;

    internal Resource(Project project, string name, ResourceType type, int uniqueId, Guid? id = null)
    {
        Project = project;
        _name = name;
        Type = type;
        UniqueId = uniqueId;
        Id = id ?? Guid.NewGuid();
    }

    public Project Project { get; }

    public Guid Id { get; }

    /// <summary>Stable numeric id, counted separately from task UIDs. Never reused.</summary>
    public int UniqueId { get; }

    public ResourceType Type { get; }

    public string Name
    {
        get => _name;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            Project.EnsureResourceNameFree(value, except: this);
            _name = value;
        }
    }

    public string? Initials { get; set; }

    public string? Group { get; set; }

    /// <summary>Peak availability (1.0 = 100%). Informational until leveling (phase 10).</summary>
    public decimal MaxUnits
    {
        get => _maxUnits;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _maxUnits = value;
        }
    }

    /// <summary>Unit-of-measure label for material resources ("t", "m³").</summary>
    public string? MaterialLabel { get; set; }

    /// <summary>When this resource's costs accrue. Stored for phases 8/9.</summary>
    public CostAccrual Accrual { get; set; } = CostAccrual.Prorated;

    /// <summary>
    /// Availability calendar (work resources). Null = the resource follows the
    /// task/project calendar. Must be one of the project's calendars.
    /// </summary>
    public WorkCalendar? Calendar
    {
        get => _calendar;
        set
        {
            if (value is not null)
            {
                if (Type != ResourceType.Work)
                {
                    throw new InvalidOperationException($"{Type} resource '{Name}' cannot have a calendar.");
                }

                if (!Project.Calendars.Any(c => ReferenceEquals(c, value)))
                {
                    throw new InvalidOperationException($"Calendar '{value.Name}' is not part of the project.");
                }
            }

            _calendar = value;
        }
    }

    /// <summary>All assignments of this resource, in creation order.</summary>
    public IReadOnlyList<Assignment> Assignments => _assignments;

    internal List<Assignment> AssignmentsList => _assignments;

    public CostRateTable RateTable(CostRateTableId table) => _rateTables[(int)table];

    /// <summary>Shorthand for the base entry of table A, the everyday rate.</summary>
    public Rate StandardRate => _rateTables[0].Entries[0].StandardRate;

    public override string ToString() => $"{Name} ({Type})";
}
