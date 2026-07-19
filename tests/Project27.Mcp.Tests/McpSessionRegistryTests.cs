using Microsoft.AspNetCore.Http;
using Project27.Mcp.Session;
using Xunit;

namespace Project27.Mcp.Tests;

public sealed class McpSessionRegistryTests
{
    private const string ServerUrl = "http://server.invalid";

    [Fact]
    public void GetOrCreate_reuses_the_same_host_for_the_same_session_id()
    {
        var registry = new McpSessionRegistry();
        var first = registry.GetOrCreate(AccessorFor("session-1", "token-a"), ServerUrl);
        var second = registry.GetOrCreate(AccessorFor("session-1", "token-b"), ServerUrl);
        Assert.Same(first, second);
    }

    [Fact]
    public void GetOrCreate_returns_distinct_hosts_for_distinct_session_ids()
    {
        var registry = new McpSessionRegistry();
        var first = registry.GetOrCreate(AccessorFor("session-1", "token-a"), ServerUrl);
        var second = registry.GetOrCreate(AccessorFor("session-2", "token-b"), ServerUrl);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void GetOrCreate_without_a_session_id_still_returns_a_usable_host()
    {
        var registry = new McpSessionRegistry();
        var host = registry.GetOrCreate(AccessorFor(sessionId: null, "token-a"), ServerUrl);
        Assert.NotNull(host);
    }

    [Fact]
    public void GetOrCreate_without_a_bearer_token_throws()
    {
        var registry = new McpSessionRegistry();
        var context = new DefaultHttpContext();
        context.Request.Headers["Mcp-Session-Id"] = "session-1";
        Assert.Throws<ProjectSessionException>(() => registry.GetOrCreate(new HttpContextAccessor { HttpContext = context }, ServerUrl));
    }

    [Fact]
    public void ExtractBearerToken_reads_the_live_header_not_a_snapshot()
    {
        // The token-refresh fix rests entirely on this: the closure McpSessionRegistry hands to
        // RemoteConnection calls ExtractBearerToken(accessor.HttpContext) fresh on every outbound
        // call, so a caller presenting a new token mid-session must be reflected immediately, not
        // stuck on whatever was true when the session's host was first created.
        var accessor = new HttpContextAccessor { HttpContext = ContextWithBearer("token-a") };
        Assert.Equal("token-a", McpSessionRegistry.ExtractBearerToken(accessor.HttpContext));

        accessor.HttpContext = ContextWithBearer("token-b");
        Assert.Equal("token-b", McpSessionRegistry.ExtractBearerToken(accessor.HttpContext));
    }

    [Fact]
    public void ExtractBearerToken_returns_null_without_an_authorization_header()
        => Assert.Null(McpSessionRegistry.ExtractBearerToken(new DefaultHttpContext()));

    [Fact]
    public async Task PruneIdleAsync_evicts_and_disposes_sessions_untouched_past_the_threshold()
    {
        var clock = new FakeTimeProvider();
        var registry = new McpSessionRegistry(clock);
        var host = registry.GetOrCreate(AccessorFor("session-1", "token-a"), ServerUrl);

        clock.Advance(TimeSpan.FromMinutes(31));
        await registry.PruneIdleAsync(TimeSpan.FromMinutes(30), TestContext.Current.CancellationToken);

        var recreated = registry.GetOrCreate(AccessorFor("session-1", "token-a"), ServerUrl);
        Assert.NotSame(host, recreated);
    }

    [Fact]
    public async Task PruneIdleAsync_keeps_sessions_touched_within_the_threshold()
    {
        var clock = new FakeTimeProvider();
        var registry = new McpSessionRegistry(clock);
        var host = registry.GetOrCreate(AccessorFor("session-1", "token-a"), ServerUrl);

        clock.Advance(TimeSpan.FromMinutes(29));
        await registry.PruneIdleAsync(TimeSpan.FromMinutes(30), TestContext.Current.CancellationToken);

        var stillSame = registry.GetOrCreate(AccessorFor("session-1", "token-a"), ServerUrl);
        Assert.Same(host, stillSame);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static HttpContextAccessor AccessorFor(string? sessionId, string token)
    {
        var context = ContextWithBearer(token);
        if (sessionId is not null)
        {
            context.Request.Headers["Mcp-Session-Id"] = sessionId;
        }

        return new HttpContextAccessor { HttpContext = context };
    }

    private static DefaultHttpContext ContextWithBearer(string token)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {token}";
        return context;
    }
}
