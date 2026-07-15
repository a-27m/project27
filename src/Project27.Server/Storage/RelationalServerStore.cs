using System.Data.Common;
using System.Globalization;

namespace Project27.Server.Storage;

/// <summary>
/// Dialect-neutral SQL implementation of <see cref="IServerStore"/>: all columns are
/// TEXT/INTEGER, upserts use `ON CONFLICT`, so SQLite and PostgreSQL share every
/// statement. Providers supply connections only.
/// </summary>
public abstract class RelationalServerStore : IServerStore
{
    protected abstract Task<DbConnection> OpenConnection(CancellationToken cancellationToken);

    public async Task Initialize(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnection(cancellationToken).ConfigureAwait(false);
        await Execute(connection, """
            CREATE TABLE IF NOT EXISTS projects(
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                created_by TEXT NOT NULL,
                created_at TEXT NOT NULL,
                version INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS snapshots(
                project_id TEXT NOT NULL,
                version INTEGER NOT NULL,
                document TEXT NOT NULL,
                saved_by TEXT NOT NULL,
                saved_at TEXT NOT NULL,
                PRIMARY KEY(project_id, version));
            CREATE TABLE IF NOT EXISTS members(
                project_id TEXT NOT NULL,
                user_id TEXT NOT NULL,
                role TEXT NOT NULL,
                PRIMARY KEY(project_id, user_id));
            CREATE TABLE IF NOT EXISTS locks(
                project_id TEXT PRIMARY KEY,
                user_id TEXT NOT NULL,
                acquired_at TEXT NOT NULL,
                refreshed_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS users(
                user_id TEXT PRIMARY KEY,
                display_name TEXT NOT NULL);
            """, cancellationToken).ConfigureAwait(false);

        // Additive migration; `ADD COLUMN IF NOT EXISTS` is Postgres-only, so swallow the duplicate error.
        try
        {
            await Execute(connection, "ALTER TABLE snapshots ADD COLUMN label TEXT", cancellationToken).ConfigureAwait(false);
        }
        catch (DbException)
        {
            // Column already exists.
        }
    }

    public async Task<IReadOnlyList<(ServerProject Project, ProjectRole Role)>> ListProjects(string userId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnection(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.id, p.name, p.created_by, p.created_at, p.version, m.role
            FROM projects p JOIN members m ON m.project_id = p.id
            WHERE m.user_id = @user
            ORDER BY p.name
            """;
        AddParameter(command, "user", userId);
        var results = new List<(ServerProject, ProjectRole)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add((ReadProject(reader), ParseRole(reader.GetString(5))));
        }

        return results;
    }

    public async Task<ServerProject?> GetProject(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnection(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, created_by, created_at, version FROM projects WHERE id = @id";
        AddParameter(command, "id", Text(id));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadProject(reader) : null;
    }

    public async Task CreateProject(ServerProject project, string documentJson, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        await using var connection = await OpenConnection(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO projects(id, name, created_by, created_at, version) VALUES (@id, @name, @by, @at, 1);
                INSERT INTO snapshots(project_id, version, document, saved_by, saved_at) VALUES (@id, 1, @doc, @by, @at);
                INSERT INTO members(project_id, user_id, role) VALUES (@id, @by, @role);
                """;
            AddParameter(command, "id", Text(project.Id));
            AddParameter(command, "name", project.Name);
            AddParameter(command, "by", project.CreatedBy);
            AddParameter(command, "at", Text(project.CreatedAt));
            AddParameter(command, "doc", documentJson);
            AddParameter(command, "role", nameof(ProjectRole.Owner));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteProject(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnection(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM snapshots WHERE project_id = @id;
            DELETE FROM members WHERE project_id = @id;
            DELETE FROM locks WHERE project_id = @id;
            DELETE FROM projects WHERE id = @id;
            """;
        AddParameter(command, "id", Text(id));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetDocument(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnection(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT document FROM snapshots
            WHERE project_id = @id
            ORDER BY version DESC LIMIT 1
            """;
        AddParameter(command, "id", Text(id));
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
    }

    public async Task<ProjectRole?> GetRole(Guid id, string userId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnection(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT role FROM members WHERE project_id = @id AND user_id = @user";
        AddParameter(command, "id", Text(id));
        AddParameter(command, "user", userId);
        var role = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return role is null ? null : ParseRole(role);
    }

    public async Task RecordUser(string userId, string displayName, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnection(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO users(user_id, display_name) VALUES (@user, @name)
                ON CONFLICT(user_id) DO UPDATE SET display_name = excluded.display_name
            """;
        AddParameter(command, "user", userId);
        AddParameter(command, "name", displayName);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetDisplayNames(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken)
    {
        var distinct = userIds.Distinct().ToList();
        var names = new Dictionary<string, string>();
        if (distinct.Count == 0)
        {
            return names;
        }

        await using var connection = await OpenConnection(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        var placeholders = string.Join(", ", distinct.Select((_, index) => $"@u{index}"));
        command.CommandText = $"SELECT user_id, display_name FROM users WHERE user_id IN ({placeholders})";
        for (var index = 0; index < distinct.Count; index++)
        {
            AddParameter(command, $"u{index}", distinct[index]);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            names[reader.GetString(0)] = reader.GetString(1);
        }

        return names;
    }

    public async Task<IReadOnlyList<ProjectMember>> GetMembers(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnection(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT user_id, role FROM members WHERE project_id = @id ORDER BY user_id";
        AddParameter(command, "id", Text(id));
        var members = new List<ProjectMember>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            members.Add(new ProjectMember(reader.GetString(0), ParseRole(reader.GetString(1))));
        }

        return members;
    }

    public async Task SetMember(Guid id, string userId, ProjectRole role, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnection(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO members(project_id, user_id, role) VALUES (@id, @user, @role)
                ON CONFLICT(project_id, user_id) DO UPDATE SET role = excluded.role
            """;
        AddParameter(command, "id", Text(id));
        AddParameter(command, "user", userId);
        AddParameter(command, "role", role.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RemoveMember(Guid id, string userId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnection(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM members WHERE project_id = @id AND user_id = @user";
        AddParameter(command, "id", Text(id));
        AddParameter(command, "user", userId);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<ProjectLock?> GetLock(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnection(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT user_id, acquired_at, refreshed_at FROM locks WHERE project_id = @id";
        AddParameter(command, "id", Text(id));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new ProjectLock(reader.GetString(0), Timestamp(reader.GetString(1)), Timestamp(reader.GetString(2)))
            : null;
    }

    public async Task<bool> TryAcquireLock(Guid id, string userId, DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnection(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        string? holder;
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT user_id FROM locks WHERE project_id = @id";
            AddParameter(read, "id", Text(id));
            holder = await read.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        }

        if (holder is not null && holder != userId)
        {
            return false;
        }

        using (var write = connection.CreateCommand())
        {
            write.Transaction = transaction;
            write.CommandText = """
                INSERT INTO locks(project_id, user_id, acquired_at, refreshed_at) VALUES (@id, @user, @now, @now)
                    ON CONFLICT(project_id) DO UPDATE SET refreshed_at = excluded.refreshed_at
                """;
            AddParameter(write, "id", Text(id));
            AddParameter(write, "user", userId);
            AddParameter(write, "now", Text(now));
            await write.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task ReleaseLock(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnection(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM locks WHERE project_id = @id";
        AddParameter(command, "id", Text(id));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int?> SaveSnapshot(Guid id, int expectedVersion, string documentJson, string name, string savedBy, DateTime now, CancellationToken cancellationToken, string? label = null)
    {
        await using var connection = await OpenConnection(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT version FROM projects WHERE id = @id";
            AddParameter(check, "id", Text(id));
            var current = await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (current is null || Convert.ToInt32(current, CultureInfo.InvariantCulture) != expectedVersion)
            {
                return null;
            }
        }

        var newVersion = expectedVersion + 1;
        using (var write = connection.CreateCommand())
        {
            write.Transaction = transaction;
            write.CommandText = """
                INSERT INTO snapshots(project_id, version, document, saved_by, saved_at, label) VALUES (@id, @version, @doc, @by, @at, @label);
                UPDATE projects SET version = @version, name = @name WHERE id = @id;
                """;
            AddParameter(write, "id", Text(id));
            AddParameter(write, "version", newVersion);
            AddParameter(write, "doc", documentJson);
            AddParameter(write, "by", savedBy);
            AddParameter(write, "at", Text(now));
            AddParameter(write, "name", name);
            AddParameter(write, "label", (object?)label ?? DBNull.Value);
            await write.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return newVersion;
    }

    public async Task<IReadOnlyList<SnapshotInfo>> GetHistory(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnection(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT version, saved_by, saved_at, label FROM snapshots
            WHERE project_id = @id ORDER BY version DESC
            """;
        AddParameter(command, "id", Text(id));
        var history = new List<SnapshotInfo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            history.Add(new SnapshotInfo(
                reader.GetInt32(0),
                reader.GetString(1),
                Timestamp(reader.GetString(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return history;
    }

    public async Task<string?> GetDocumentAt(Guid id, int version, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnection(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT document FROM snapshots WHERE project_id = @id AND version = @version";
        AddParameter(command, "id", Text(id));
        AddParameter(command, "version", version);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
    }

    public async Task<bool> SetSnapshotLabel(Guid id, int version, string? label, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnection(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE snapshots SET label = @label WHERE project_id = @id AND version = @version";
        AddParameter(command, "label", (object?)label ?? DBNull.Value);
        AddParameter(command, "id", Text(id));
        AddParameter(command, "version", version);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    // ---------------------------------------------------------------- helpers

    private static async Task Execute(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static ServerProject ReadProject(DbDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        reader.GetString(2),
        Timestamp(reader.GetString(3)),
        reader.GetInt32(4));

    private static ProjectRole ParseRole(string text) => Enum.Parse<ProjectRole>(text, ignoreCase: true);

    private static string Text(Guid id) => id.ToString("D");

    private static string Text(DateTime timestamp) => timestamp.ToString("O", CultureInfo.InvariantCulture);

    private static DateTime Timestamp(string text)
        => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
