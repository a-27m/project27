using Project27.Core;

namespace Project27.Storage;

/// <summary>
/// Snapshot persistence for a single project. Implementations: SQLite `.p27` files
/// (this assembly), PostgreSQL (server host).
/// </summary>
public interface IProjectStore
{
    public Project Load();

    public void Save(Project project);
}
