using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Project27.Core;
using Project27.Core.Persistence;

namespace Project27.Storage;

/// <summary>
/// A `.p27` local project file: SQLite with a metadata table and a single JSON
/// snapshot of the <see cref="ProjectDocument"/>. Snapshot-per-save keeps the format
/// trivially migratable; the store never keeps the file handle open between calls.
/// </summary>
public sealed class SqliteProjectStore : IProjectStore
{
    public const string FileExtension = ".p27";

    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private SqliteProjectStore(string path) => Path = path;

    public string Path { get; }

    /// <summary>Creates a new project file; fails if the file already exists.</summary>
    public static SqliteProjectStore Create(string path, Project project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(project);
        if (File.Exists(path))
        {
            throw new IOException($"'{path}' already exists.");
        }

        var store = new SqliteProjectStore(path);
        store.Save(project);
        return store;
    }

    /// <summary>Opens an existing project file.</summary>
    public static SqliteProjectStore Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Project file '{path}' not found.", path);
        }

        return new SqliteProjectStore(path);
    }

    public Project Load()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = 'format_version'";
        var version = Convert.ToInt32(command.ExecuteScalar() as string, CultureInfo.InvariantCulture);
        if (version != FormatVersion)
        {
            throw new NotSupportedException($"'{Path}' uses format version {version}; this build supports {FormatVersion}.");
        }

        command.CommandText = "SELECT json FROM snapshot WHERE id = 1";
        var json = command.ExecuteScalar() as string
            ?? throw new InvalidDataException($"'{Path}' contains no project snapshot.");
        var document = JsonSerializer.Deserialize<ProjectDocument>(json, JsonOptions)
            ?? throw new InvalidDataException($"'{Path}' contains an empty project snapshot.");
        var project = ProjectDocumentMapper.FromDocument(document);
        project.Recalculate();
        return project;
    }

    public void Save(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var json = JsonSerializer.Serialize(ProjectDocumentMapper.ToDocument(project), JsonOptions);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS snapshot(id INTEGER PRIMARY KEY CHECK (id = 1), json TEXT NOT NULL, saved_at TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();

        command.CommandText = """
            INSERT INTO meta(key, value) VALUES ('format_version', $version), ('project_name', $name)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            INSERT INTO snapshot(id, json, saved_at) VALUES (1, $json, $savedAt)
                ON CONFLICT(id) DO UPDATE SET json = excluded.json, saved_at = excluded.saved_at;
            """;
        command.Parameters.AddWithValue("$version", FormatVersion.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$name", project.Name);
        command.Parameters.AddWithValue("$json", json);
        command.Parameters.AddWithValue("$savedAt", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Pooling = false, // keep the file closed between operations; .p27 files are copied/synced around
        }.ConnectionString);
        connection.Open();
        return connection;
    }
}
