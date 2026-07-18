using Project27.Core;
using Project27.Core.Commands;
using Project27.Core.Reports;
using Project27.Core.Scheduling;
using Project27.Core.Usage;
using Project27.Storage;
using CoreViews = Project27.Core.Views;

namespace Project27.Mcp.Session;

/// <summary>A local `.p27` file, opened once for the process lifetime (mirrors CLI local mode).</summary>
public sealed class LocalProjectSession : IProjectSession
{
    private readonly SqliteProjectStore _store;
    private readonly Project _project;

    private LocalProjectSession(SqliteProjectStore store, Project project)
    {
        _store = store;
        _project = project;
    }

    public static LocalProjectSession Open(string path)
    {
        var store = SqliteProjectStore.Open(path);
        var project = store.Load();
        project.Recalculate();
        return new LocalProjectSession(store, project);
    }

    /// <summary>Creates a brand-new `.p27` file at <paramref name="path"/>; fails if it already exists.</summary>
    public static LocalProjectSession Create(string path, string name, DateTime? start)
    {
        var project = new Project(name, start ?? DateTime.Today.AddHours(8));
        project.Recalculate();
        var store = SqliteProjectStore.Create(path, project);
        return new LocalProjectSession(store, project);
    }

    public Task<ProjectSummary> GetProjectAsync(CancellationToken cancellationToken)
    {
        var stats = ProjectStats.For(_project);
        var evm = EarnedValue.ForProject(_project);
        return Task.FromResult(new ProjectSummary(
            _project.Id,
            _project.Name,
            _project.StartDate,
            _project.FinishDate,
            _project.ScheduleFrom,
            _project.Calendar.Name,
            _project.StatusDate,
            _project.TotalWorkMinutes,
            _project.TotalCost,
            [.. _project.Calendars.Select(c => c.Name)],
            [.. _project.Resources.Select(ResourceSummaryOf)],
            [.. _project.CustomFields.Select(f => new CustomFieldSummary(f.Id, f.Alias, f.Kind.ToString(), f.Formula is not null))],
            stats,
            evm));
    }

    public Task<TaskView> ListTasksAsync(
        IReadOnlyList<string>? fields, string? table, string? filter, string? sort, string? groupBy, CancellationToken cancellationToken)
    {
        var fieldKeys = fields is { Count: > 0 }
            ? fields
            : CoreViews.TaskView.Tables.TryGetValue(table ?? "entry", out var tableFields)
                ? tableFields
                : throw new KeyNotFoundException($"Unknown table '{table}'.");
        var definition = new CoreViews.ViewDefinition(
            fieldKeys,
            filter is { Length: > 0 } ? CoreViews.FilterParser.Parse(_project, filter) : null,
            sort is { Length: > 0 } ? CoreViews.TaskView.ParseSorts(sort) : null,
            string.IsNullOrWhiteSpace(groupBy) ? null : groupBy);
        var result = CoreViews.TaskView.Evaluate(_project, definition);
        return Task.FromResult(new TaskView(
            [.. result.Fields.Select(f => new ViewField(f.Key, f.Caption, f.Kind.ToString()))],
            [.. result.Groups.Select(g => new ViewGroup(
                g.Heading,
                [.. g.Rows.Select(r => new ViewRow(r.Task.UniqueId, r.Task.RowNumber, r.Cells.ToDictionary(c => c.Field, c => c.Raw)))]))]));
    }

    public Task<IReadOnlyList<ResourceSummary>> ListResourcesAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ResourceSummary>>([.. _project.Resources.Select(ResourceSummaryOf)]);

    public Task<IReadOnlyList<TaskDriver>> GetTaskDriversAsync(int uid, CancellationToken cancellationToken)
    {
        var task = _project.Tasks.FirstOrDefault(t => t.UniqueId == uid)
            ?? throw new KeyNotFoundException($"No task with uid {uid}.");
        return Task.FromResult<IReadOnlyList<TaskDriver>>(
            [.. TaskDrivers.Explain(task).Select(d => new TaskDriver(d.Kind.ToString(), d.Description, d.Binding, d.Date, d.PredecessorUid))]);
    }

    public Task<UsageResult> GetUsageAsync(bool weekly, CancellationToken cancellationToken)
    {
        var leaves = _project.Tasks.Where(t => !t.IsSummary && t.IsActive).ToList();
        var rows = new List<UsageRow>();
        foreach (var task in leaves)
        {
            var daily = Timephased.ForTask(task);
            var buckets = weekly ? Timephased.ByWeek(daily, _project.TimeSettings.WeekStartsOn) : daily;
            rows.Add(new UsageRow(
                task.UniqueId,
                task.RowNumber,
                task.Name,
                task.OutlineLevel,
                task.IsSummary,
                [.. buckets.Select(b => new UsageBucket(b.Date, b.WorkMinutes, b.Cost))],
                buckets.Sum(b => b.WorkMinutes),
                buckets.Sum(b => b.Cost)));
        }

        return Task.FromResult(new UsageResult(weekly ? "week" : "day", _project.TimeSettings.WeekStartsOn, rows));
    }

    public Task<string> GetReportAsync(string name, CancellationToken cancellationToken) => Task.FromResult(ReportBuilder.Render(_project, name));

    public Task<CommandResult> ApplyAsync(IReadOnlyList<ProjectCommand> commands, CancellationToken cancellationToken)
    {
        var createdUids = CommandExecutor.ApplyAll(_project, commands);
        _project.Recalculate();
        _store.Save(_project);
        return Task.FromResult(new CommandResult(createdUids));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static ResourceSummary ResourceSummaryOf(Resource resource)
        => new(resource.UniqueId, resource.Name, resource.Type, resource.MaxUnits, resource.StandardRate.ToString());
}
