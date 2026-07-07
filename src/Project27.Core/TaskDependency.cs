namespace Project27.Core;

/// <summary>A dependency link between two tasks. Created via <see cref="Project.Link"/>.</summary>
public sealed class TaskDependency
{
    internal TaskDependency(ProjectTask predecessor, ProjectTask successor, DependencyType type, Lag lag)
    {
        Predecessor = predecessor;
        Successor = successor;
        Type = type;
        Lag = lag;
    }

    public ProjectTask Predecessor { get; }

    public ProjectTask Successor { get; }

    public DependencyType Type { get; set; }

    public Lag Lag { get; set; }

    public override string ToString() => $"{Predecessor.Name} -[{Type}{(Lag.IsZero ? string.Empty : "+" + Lag)}]-> {Successor.Name}";
}
