using Project27.Core;

namespace Project27.Mcp.Session;

// Mcp-owned wire shapes (E22: projections are consumer-owned). Mirrors the shape of
// Project27.Server's ScheduleProjection DTOs field-for-field so RemoteProjectSession can
// deserialize the server's JSON straight into these types; LocalProjectSession builds the
// same shapes from Core directly.

public sealed record ResourceSummary(int Uid, string Name, ResourceType Type, decimal MaxUnits, string Rate);

public sealed record CustomFieldSummary(string Id, string? Alias, string Kind, bool HasFormula);

public sealed record ProjectSummary(
    Guid Id,
    string Name,
    DateTime Start,
    DateTime? Finish,
    ScheduleFrom ScheduleFrom,
    string Calendar,
    DateTime? StatusDate,
    decimal TotalWorkMinutes,
    decimal TotalCost,
    IReadOnlyList<string> Calendars,
    IReadOnlyList<ResourceSummary> Resources,
    IReadOnlyList<CustomFieldSummary> CustomFields,
    ProjectStatsData Stats,
    EarnedValueData Evm);

public sealed record ViewField(string Key, string Caption, string Kind);

public sealed record ViewRow(int Uid, int Id, IReadOnlyDictionary<string, object?> Values);

public sealed record ViewGroup(string? Heading, IReadOnlyList<ViewRow> Rows);

public sealed record TaskView(IReadOnlyList<ViewField> Fields, IReadOnlyList<ViewGroup> Groups);

public sealed record UsageBucket(DateOnly Date, decimal WorkMinutes, decimal Cost);

public sealed record UsageRow(
    int Uid,
    int Row,
    string Name,
    int OutlineLevel,
    bool Summary,
    IReadOnlyList<UsageBucket> Buckets,
    decimal TotalWorkMinutes,
    decimal TotalCost);

public sealed record UsageResult(string Granularity, DayOfWeek WeekStartsOn, IReadOnlyList<UsageRow> Rows);

public sealed record TaskDriver(string Kind, string Description, bool Binding, DateTime? Date, int? PredecessorUid);

/// <summary>Result of applying a command batch: created-task uids, aligned with the batch (null for non-creating commands).</summary>
public sealed record CommandResult(IReadOnlyList<int?> CreatedUids);
