using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Project27.Server.Tests;

/// <summary>
/// In-process server with DevAuth and a per-fixture SQLite store in a temp
/// directory. <see cref="Client"/> returns an authenticated client for a dev user.
/// </summary>
public sealed class ServerFixture : IDisposable
{
    private readonly string _directory;
    private readonly WebApplicationFactory<Program> _factory;

    public ServerFixture()
        : this(staleAfterMinutes: 30)
    {
    }

    /// <summary>Variant for lock-staleness tests; xunit needs the default ctor above.</summary>
    public static ServerFixture WithStaleMinutes(int staleAfterMinutes) => new(staleAfterMinutes);

    private ServerFixture(int staleAfterMinutes)
    {
        _directory = Path.Combine(Path.GetTempPath(), "p27-server-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Storage:Path", Path.Combine(_directory, "server.db"));
            builder.UseSetting("Auth:DevAuth", "true");
            builder.UseSetting("Locking:StaleAfterMinutes", staleAfterMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        });
    }

    public WebApplicationFactory<Program> Factory => _factory;

    public HttpClient Client(string? user = "alice")
    {
        var client = _factory.CreateClient();
        if (user is not null)
        {
            client.DefaultRequestHeaders.Add("X-Dev-User", user);
        }

        return client;
    }

    public void Dispose()
    {
        _factory.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Temp dir cleanup is best-effort.
        }
    }
}

[CollectionDefinition("server")]
public sealed class ServerTestGroup : ICollectionFixture<ServerFixture>;
