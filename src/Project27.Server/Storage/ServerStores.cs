using System.Data.Common;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Project27.Server.Storage;

/// <summary>SQLite-backed server store: the default for development and small installs.</summary>
public sealed class SqliteServerStore : RelationalServerStore
{
    private readonly string _connectionString;

    public SqliteServerStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ConnectionString;
    }

    protected override async Task<DbConnection> OpenConnection(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}

/// <summary>PostgreSQL-backed server store for production deployments.</summary>
public sealed class PostgresServerStore : RelationalServerStore, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresServerStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    protected override async Task<DbConnection> OpenConnection(CancellationToken cancellationToken)
        => await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}

public static class ServerStoreFactory
{
    /// <summary>Builds the store from `Storage:Provider` (+ `Path` / `ConnectionString`).</summary>
    public static IServerStore Create(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var provider = configuration["Storage:Provider"] ?? "sqlite";
        return provider.ToUpperInvariant() switch
        {
            "SQLITE" => new SqliteServerStore(configuration["Storage:Path"] ?? "project27-server.db"),
            "POSTGRES" => new PostgresServerStore(
                configuration["Storage:ConnectionString"]
                    ?? throw new InvalidOperationException("Storage:ConnectionString is required for the postgres provider.")),
            _ => throw new InvalidOperationException($"Unknown storage provider '{provider}'; use sqlite or postgres."),
        };
    }
}
