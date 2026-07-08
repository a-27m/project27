using Project27.Core.Scheduling;
using Project27.Core.Time;

namespace Project27.Core;

/// <summary>
/// The project aggregate: outline of tasks, dependencies, calendars, and settings.
/// All mutations go through this class; call <see cref="Recalculate"/> to reschedule.
/// </summary>
public sealed class Project
{
    private const int MaxRecurringOccurrences = 999;

    private readonly ProjectTask _root;
    private readonly List<WorkCalendar> _calendars = [];
    private readonly List<Resource> _resources = [];
    private List<ProjectTask>? _flattened;
    private int _nextUniqueId;
    private int _nextResourceUniqueId;

    public Project(string name, DateTime start, WorkCalendar? calendar = null, Guid? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id ?? Guid.NewGuid();
        Name = name;
        StartDate = start;
        Calendar = calendar ?? WorkCalendar.CreateStandard();
        _calendars.Add(Calendar);
        _root = new ProjectTask(this, "<project root>", uniqueId: 0) { OutlineLevel = -1 };
    }

    public Guid Id { get; }

    public string Name { get; set; }

    public TimeSettings TimeSettings { get; } = new();

    /// <summary>Anchor for schedule-from-start projects; computed for schedule-from-finish.</summary>
    public DateTime StartDate { get; set; }

    /// <summary>Anchor for schedule-from-finish projects; computed for schedule-from-start.</summary>
    public DateTime? FinishDate { get; set; }

    public ScheduleFrom ScheduleFrom { get; set; } = ScheduleFrom.ProjectStart;

    /// <summary>The project calendar used when tasks have no calendar of their own.</summary>
    public WorkCalendar Calendar { get; set; }

    public IReadOnlyList<WorkCalendar> Calendars => _calendars;

    /// <summary>Tasks with total slack at or below this count as critical. Default 0.</summary>
    public decimal CriticalSlackThresholdMinutes { get; set; }

    internal ProjectTask Root => _root;

    /// <summary>All tasks in outline (pre-order) row order.</summary>
    public IReadOnlyList<ProjectTask> Tasks
    {
        get
        {
            if (_flattened is null)
            {
                _flattened = [.. _root.SelfAndDescendants().Skip(1)];
                RefreshOutline();
            }

            return _flattened;
        }
    }

    public void AddCalendar(WorkCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        if (_calendars.Any(c => c.Id == calendar.Id))
        {
            throw new InvalidOperationException($"Calendar '{calendar.Name}' is already part of the project.");
        }

        _calendars.Add(calendar);
    }

    public bool RemoveCalendar(WorkCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        if (ReferenceEquals(calendar, Calendar))
        {
            throw new InvalidOperationException("The project calendar cannot be removed.");
        }

        if (Tasks.Any(t => ReferenceEquals(t.Calendar, calendar)))
        {
            throw new InvalidOperationException($"Calendar '{calendar.Name}' is used by tasks.");
        }

        if (_resources.Any(r => ReferenceEquals(r.Calendar, calendar)))
        {
            throw new InvalidOperationException($"Calendar '{calendar.Name}' is used by resources.");
        }

        return _calendars.Remove(calendar);
    }

    // ------------------------------------------------------------- resources

    /// <summary>All resources in creation order.</summary>
    public IReadOnlyList<Resource> Resources => _resources;

    public Resource AddResource(string name, ResourceType type = ResourceType.Work)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        EnsureResourceNameFree(name);
        var resource = new Resource(this, name, type, ++_nextResourceUniqueId);
        _resources.Add(resource);
        return resource;
    }

    /// <summary>Removes a resource and drops its assignments (no effort-driven redistribution).</summary>
    public bool RemoveResource(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (!_resources.Contains(resource))
        {
            return false;
        }

        foreach (var assignment in resource.AssignmentsList.ToList())
        {
            RemoveAssignmentCore(assignment);
        }

        return _resources.Remove(resource);
    }

    internal void EnsureResourceNameFree(string name, Resource? except = null)
    {
        if (_resources.Any(r => !ReferenceEquals(r, except) && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"A resource named '{name}' already exists.");
        }
    }

    /// <summary>
    /// Assigns a resource to a leaf task. For work resources, omitted work defaults to
    /// Duration × Units; on effort-driven tasks that already have work assignments,
    /// omitting work redistributes the existing total instead
    /// (docs/spec/04-resources-costs.md).
    /// </summary>
    public Assignment Assign(ProjectTask task, Resource resource, decimal? units = null, Duration? work = null)
    {
        EnsureOwned(task);
        ArgumentNullException.ThrowIfNull(resource);
        if (!_resources.Contains(resource))
        {
            throw new InvalidOperationException($"Resource '{resource.Name}' does not belong to this project.");
        }

        if (task.IsSummary)
        {
            throw new InvalidOperationException($"'{task.Name}' is a summary task; assign resources to its subtasks.");
        }

        if (task.AssignmentsList.Any(a => ReferenceEquals(a.Resource, resource)))
        {
            throw new InvalidOperationException($"'{resource.Name}' is already assigned to '{task.Name}'.");
        }

        if (work is not null && resource.Type != ResourceType.Work)
        {
            throw new ArgumentException($"{resource.Type} resource '{resource.Name}' carries no work.", nameof(work));
        }

        if (units is { } u)
        {
            if (resource.Type == ResourceType.Work)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(u, nameof(units));
            }
            else
            {
                ArgumentOutOfRangeException.ThrowIfNegative(u, nameof(units));
            }
        }

        var assignment = new Assignment(task, resource) { Units = units ?? 1m };
        if (resource.Type == ResourceType.Work)
        {
            var redistribute = task.IsEffortDriven
                && work is null
                && EffortTriangle.WorkAssignments(task).Any(a => a.WorkMinutes > 0);
            var total = redistribute ? EffortTriangle.WorkAssignments(task).Sum(a => a.WorkMinutes) : 0m;

            task.AssignmentsList.Add(assignment);
            resource.AssignmentsList.Add(assignment);

            if (work is { } w)
            {
                var minutes = w.ToMinutes(TimeSettings);
                ArgumentOutOfRangeException.ThrowIfNegative(minutes, nameof(work));
                assignment.WorkMinutes = minutes;
                if (units is null && task.Type == TaskType.FixedDuration && task.DurationMinutes > 0)
                {
                    assignment.Units = minutes / task.DurationMinutes;
                }

                EffortTriangle.OnAssignmentEdited(assignment, TriangleEdit.Work);
            }
            else if (redistribute)
            {
                EffortTriangle.Redistribute(task, total);
            }
            else
            {
                assignment.WorkMinutes = EffortTriangle.ImpliedWork(task, assignment.Units, assignment.Contour);
            }
        }
        else
        {
            task.AssignmentsList.Add(assignment);
            resource.AssignmentsList.Add(assignment);
        }

        return assignment;
    }

    /// <summary>Removes an assignment; effort-driven tasks keep their total work.</summary>
    public void Unassign(Assignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        if (!ReferenceEquals(assignment.Task.Project, this) || !assignment.Task.AssignmentsList.Contains(assignment))
        {
            throw new InvalidOperationException("The assignment does not belong to this project.");
        }

        var task = assignment.Task;
        var redistribute = assignment.Resource.Type == ResourceType.Work
            && task.IsEffortDriven
            && assignment.WorkMinutes > 0;
        var total = redistribute ? EffortTriangle.WorkAssignments(task).Sum(a => a.WorkMinutes) : 0m;

        RemoveAssignmentCore(assignment);

        if (redistribute && EffortTriangle.WorkAssignments(task).Any())
        {
            EffortTriangle.Redistribute(task, total);
        }
    }

    private static void RemoveAssignmentCore(Assignment assignment)
    {
        assignment.Task.AssignmentsList.Remove(assignment);
        assignment.Resource.AssignmentsList.Remove(assignment);
    }

    /// <summary>Total cost over active top-level tasks (assignment costs are outputs of Recalculate).</summary>
    public decimal TotalCost => _root.ChildrenList.Where(t => t.IsActive).Sum(t => t.Cost);

    /// <summary>Total work in minutes over active top-level tasks.</summary>
    public decimal TotalWorkMinutes => _root.ChildrenList.Where(t => t.IsActive).Sum(t => t.WorkMinutes);

    public ProjectTask AddTask(string name, Duration? duration = null, ProjectTask? parent = null, int? at = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var target = parent ?? _root;
        if (parent is not null)
        {
            EnsureOwned(parent);
        }

        var task = new ProjectTask(this, name, ++_nextUniqueId) { Parent = target };
        if (duration is { } d)
        {
            task.Duration = d;
        }

        if (ScheduleFrom == ScheduleFrom.ProjectFinish)
        {
            task.SetConstraint(ConstraintType.AsLateAsPossible);
        }

        var index = at ?? target.ChildrenList.Count;
        target.ChildrenList.Insert(index, task);
        InvalidateOutline();
        return task;
    }

    public ProjectTask AddMilestone(string name, ProjectTask? parent = null)
        => AddTask(name, new Duration(0, DurationUnit.Days), parent);

    /// <summary>Removes a task and its whole subtree, dropping every dependency touching it.</summary>
    public void RemoveTask(ProjectTask task)
    {
        EnsureOwned(task);
        foreach (var member in task.SelfAndDescendants().ToList())
        {
            foreach (var dependency in member.PredecessorsList.Concat(member.SuccessorsList).ToList())
            {
                Unlink(dependency);
            }

            foreach (var assignment in member.AssignmentsList.ToList())
            {
                RemoveAssignmentCore(assignment);
            }
        }

        task.Parent!.ChildrenList.Remove(task);
        task.Parent = null;
        InvalidateOutline();
    }

    /// <summary>Moves a task (with its subtree) under a new parent at the given child index.</summary>
    public void MoveTask(ProjectTask task, ProjectTask? newParent, int index)
    {
        EnsureOwned(task);
        var target = newParent ?? _root;
        if (!target.IsRoot)
        {
            EnsureOwned(target);
        }

        if (ReferenceEquals(task, target) || task.IsAncestorOf(target))
        {
            throw new InvalidOperationException("A task cannot be moved under itself.");
        }

        task.Parent!.ChildrenList.Remove(task);
        task.Parent = target;
        target.ChildrenList.Insert(Math.Min(index, target.ChildrenList.Count), task);
        EnsureNoLineageLinks(task);
        InvalidateOutline();
    }

    /// <summary>Makes the task a child of its preceding sibling (Tab in MS Project).</summary>
    public void Indent(ProjectTask task)
    {
        EnsureOwned(task);
        var siblings = task.Parent!.ChildrenList;
        var index = siblings.IndexOf(task);
        if (index == 0)
        {
            throw new InvalidOperationException($"'{task.Name}' has no preceding sibling to indent under.");
        }

        MoveTask(task, siblings[index - 1], int.MaxValue);
    }

    /// <summary>Moves the task to be the next sibling of its parent (Shift+Tab in MS Project).</summary>
    public void Outdent(ProjectTask task)
    {
        EnsureOwned(task);
        var parent = task.Parent!;
        if (parent.IsRoot)
        {
            throw new InvalidOperationException($"'{task.Name}' is already at the outermost level.");
        }

        var grandParent = parent.Parent!;
        MoveTask(task, grandParent.IsRoot ? null : grandParent, grandParent.ChildrenList.IndexOf(parent) + 1);
    }

    public TaskDependency Link(ProjectTask predecessor, ProjectTask successor, DependencyType type = DependencyType.FinishToStart, Lag lag = default)
    {
        EnsureOwned(predecessor);
        EnsureOwned(successor);
        if (ReferenceEquals(predecessor, successor))
        {
            throw new InvalidOperationException("A task cannot depend on itself.");
        }

        if (predecessor.IsAncestorOf(successor) || successor.IsAncestorOf(predecessor))
        {
            throw new InvalidOperationException($"'{predecessor.Name}' and '{successor.Name}' are in the same outline lineage and cannot be linked.");
        }

        if (predecessor.SuccessorsList.Any(d => ReferenceEquals(d.Successor, successor)))
        {
            throw new InvalidOperationException($"'{predecessor.Name}' is already linked to '{successor.Name}'.");
        }

        if (WouldCreateCycle(predecessor, successor))
        {
            throw new InvalidOperationException($"Linking '{predecessor.Name}' to '{successor.Name}' would create a dependency cycle.");
        }

        var dependency = new TaskDependency(predecessor, successor, type, lag);
        predecessor.SuccessorsList.Add(dependency);
        successor.PredecessorsList.Add(dependency);
        return dependency;
    }

    public void Unlink(TaskDependency dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        if (!ReferenceEquals(dependency.Successor.Project, this))
        {
            throw new InvalidOperationException("The dependency does not belong to this project.");
        }

        dependency.Predecessor.SuccessorsList.Remove(dependency);
        dependency.Successor.PredecessorsList.Remove(dependency);
    }

    /// <summary>
    /// Creates a recurring task: a summary whose children occur per
    /// <paramref name="recurrence"/>, each pinned with a start-no-earlier-than
    /// constraint. Requires <paramref name="until"/> or <paramref name="occurrences"/>.
    /// </summary>
    public ProjectTask AddRecurringTask(
        string name,
        Duration occurrenceDuration,
        Recurrence recurrence,
        DateOnly from,
        DateOnly? until = null,
        int? occurrences = null,
        ProjectTask? parent = null)
    {
        ArgumentNullException.ThrowIfNull(recurrence);
        if (until is null && occurrences is null)
        {
            throw new ArgumentException("A recurring task needs an end date or an occurrence count.", nameof(until));
        }

        if (occurrences is { } n)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(n, 1, nameof(occurrences));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(n, MaxRecurringOccurrences, nameof(occurrences));
        }

        var horizon = until ?? from.AddYears(100);
        var dates = recurrence.Occurrences(from, horizon)
            .Take(Math.Min(occurrences ?? MaxRecurringOccurrences, MaxRecurringOccurrences))
            .ToList();
        if (dates.Count == 0)
        {
            throw new ArgumentException("The recurrence produces no occurrences in the given window.", nameof(recurrence));
        }

        var summary = AddTask(name, parent: parent);
        summary.IsRecurring = true;
        for (var i = 0; i < dates.Count; i++)
        {
            var child = AddTask($"{name} {i + 1}", occurrenceDuration, summary);
            child.SetConstraint(
                ConstraintType.StartNoEarlierThan,
                dates[i].ToDateTime(TimeSettings.DefaultStartTime));
        }

        return summary;
    }

    /// <summary>Runs the full forward/backward scheduling passes. Deterministic.</summary>
    public void Recalculate() => ProjectScheduler.Recalculate(this);

    internal void InvalidateOutline() => _flattened = null;

    internal void EnsureOutline() => _ = Tasks;

    internal ProjectTask RestoreTask(string name, int uniqueId, Guid id, ProjectTask? parent)
    {
        var task = new ProjectTask(this, name, uniqueId, id) { Parent = parent ?? _root };
        task.Parent!.ChildrenList.Add(task);
        _nextUniqueId = Math.Max(_nextUniqueId, uniqueId);
        InvalidateOutline();
        return task;
    }

    internal Resource RestoreResource(string name, ResourceType type, int uniqueId, Guid id)
    {
        var resource = new Resource(this, name, type, uniqueId, id);
        _resources.Add(resource);
        _nextResourceUniqueId = Math.Max(_nextResourceUniqueId, uniqueId);
        return resource;
    }

    /// <summary>Reattaches an assignment without triangle recalculation (documents store all corners).</summary>
    internal Assignment RestoreAssignment(ProjectTask task, Resource resource, Guid id)
    {
        if (!ReferenceEquals(task.Project, this) || !_resources.Contains(resource))
        {
            throw new InvalidOperationException("Restored assignment references foreign members.");
        }

        var assignment = new Assignment(task, resource, id);
        task.AssignmentsList.Add(assignment);
        resource.AssignmentsList.Add(assignment);
        return assignment;
    }

    internal TaskDependency RestoreLink(ProjectTask predecessor, ProjectTask successor, DependencyType type, Lag lag)
    {
        if (!ReferenceEquals(predecessor.Project, this) || !ReferenceEquals(successor.Project, this))
        {
            throw new InvalidOperationException("Restored dependency references foreign tasks.");
        }

        var dependency = new TaskDependency(predecessor, successor, type, lag);
        predecessor.SuccessorsList.Add(dependency);
        successor.PredecessorsList.Add(dependency);
        return dependency;
    }

    private void RefreshOutline()
    {
        var row = 0;
        Number(_root, string.Empty, -1);

        void Number(ProjectTask task, string prefix, int level)
        {
            if (!task.IsRoot)
            {
                task.RowNumber = ++row;
                task.OutlineLevel = level;
                task.OutlineNumber = prefix;
            }

            for (var i = 0; i < task.ChildrenList.Count; i++)
            {
                var childPrefix = task.IsRoot
                    ? (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : $"{prefix}.{i + 1}";
                Number(task.ChildrenList[i], childPrefix, level + 1);
            }
        }
    }

    private void EnsureOwned(ProjectTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (!ReferenceEquals(task.Project, this) || (task.Parent is null && !task.IsRoot))
        {
            throw new InvalidOperationException($"Task '{task.Name}' does not belong to this project.");
        }

        if (task.IsRoot)
        {
            throw new InvalidOperationException("The hidden root task cannot be targeted directly.");
        }
    }

    private static void EnsureNoLineageLinks(ProjectTask moved)
    {
        foreach (var task in moved.SelfAndDescendants())
        {
            foreach (var dependency in task.PredecessorsList.Concat(task.SuccessorsList))
            {
                var other = ReferenceEquals(dependency.Successor, task) ? dependency.Predecessor : dependency.Successor;
                if (task.IsAncestorOf(other) || other.IsAncestorOf(task))
                {
                    throw new InvalidOperationException(
                        $"Moving '{moved.Name}' would place linked tasks '{task.Name}' and '{other.Name}' in the same lineage.");
                }
            }
        }
    }

    /// <summary>
    /// Cycle check over the leaf-expanded graph: a link to/from a summary behaves as
    /// links to/from all its leaves, and rollup couples children to ancestors.
    /// </summary>
    private static bool WouldCreateCycle(ProjectTask predecessor, ProjectTask successor)
    {
        // Influence-graph DFS: does the successor already influence the predecessor?
        // Edges: child → parent (rollup); link pred → succ.SelfAndDescendants (a link
        // into a summary constrains its whole subtree). The new link would make
        // predecessor influence successor's subtree, so start the search there.
        var visited = new HashSet<ProjectTask>();
        var stack = new Stack<ProjectTask>();
        foreach (var seed in successor.SelfAndDescendants())
        {
            Push(seed);
        }

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (ReferenceEquals(current, predecessor))
            {
                return true;
            }

            if (current.Parent is { IsRoot: false } parent)
            {
                Push(parent);
            }

            foreach (var dependency in current.SuccessorsList)
            {
                foreach (var reached in dependency.Successor.SelfAndDescendants())
                {
                    Push(reached);
                }
            }
        }

        return false;

        void Push(ProjectTask task)
        {
            if (visited.Add(task))
            {
                stack.Push(task);
            }
        }
    }
}
