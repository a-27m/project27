namespace Project27.Server.Storage;

/// <summary>
/// Server-side project storage: metadata, versioned snapshots, members, locks.
/// Distinct from the file-oriented <c>IProjectStore</c>; documents are opaque JSON
/// here (validated by the endpoint layer via the engine).
/// </summary>
public interface IServerStore
{
    public Task Initialize(CancellationToken cancellationToken);

    public Task<IReadOnlyList<(ServerProject Project, ProjectRole Role)>> ListProjects(string userId, CancellationToken cancellationToken);

    public Task<ServerProject?> GetProject(Guid id, CancellationToken cancellationToken);

    /// <summary>Creates the project at version 1 with its first snapshot and the creator as owner.</summary>
    public Task CreateProject(ServerProject project, string documentJson, CancellationToken cancellationToken);

    public Task DeleteProject(Guid id, CancellationToken cancellationToken);

    /// <summary>Latest snapshot's JSON, or null when the project does not exist.</summary>
    public Task<string?> GetDocument(Guid id, CancellationToken cancellationToken);

    public Task<ProjectRole?> GetRole(Guid id, string userId, CancellationToken cancellationToken);

    /// <summary>Remembers the caller's display name, refreshed on every authenticated request.</summary>
    public Task RecordUser(string userId, string displayName, CancellationToken cancellationToken);

    /// <summary>Display names for the given user ids; ids never seen before are omitted.</summary>
    public Task<IReadOnlyDictionary<string, string>> GetDisplayNames(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken);

    public Task<IReadOnlyList<ProjectMember>> GetMembers(Guid id, CancellationToken cancellationToken);

    public Task SetMember(Guid id, string userId, ProjectRole role, CancellationToken cancellationToken);

    public Task<bool> RemoveMember(Guid id, string userId, CancellationToken cancellationToken);

    public Task<ProjectLock?> GetLock(Guid id, CancellationToken cancellationToken);

    /// <summary>Acquires the lock, or refreshes it when already held by the same user. False when another user holds it.</summary>
    public Task<bool> TryAcquireLock(Guid id, string userId, DateTime now, CancellationToken cancellationToken);

    public Task ReleaseLock(Guid id, CancellationToken cancellationToken);

    /// <summary>Appends a snapshot when the current version matches; returns the new version, or null on a version conflict.</summary>
    public Task<int?> SaveSnapshot(Guid id, int expectedVersion, string documentJson, string name, string savedBy, DateTime now, CancellationToken cancellationToken, string? label = null);

    /// <summary>Version history, newest first.</summary>
    public Task<IReadOnlyList<SnapshotInfo>> GetHistory(Guid id, CancellationToken cancellationToken);

    /// <summary>A specific version's document JSON; null when absent.</summary>
    public Task<string?> GetDocumentAt(Guid id, int version, CancellationToken cancellationToken);

    /// <summary>Names (or renames; null clears) a stored version.</summary>
    public Task<bool> SetSnapshotLabel(Guid id, int version, string? label, CancellationToken cancellationToken);

    /// <summary>The caller's stored UI preferences (opaque JSON), or null when never saved.</summary>
    public Task<string?> GetPreferences(Guid id, string userId, CancellationToken cancellationToken);

    /// <summary>Upserts the caller's UI preferences. Independent of the document version and checkout lock.</summary>
    public Task SetPreferences(Guid id, string userId, string dataJson, DateTime now, CancellationToken cancellationToken);
}

public sealed record SnapshotInfo(int Version, string SavedBy, DateTime SavedAt, string? Label);
