using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;

namespace Project27.Mcp.Session;

/// <summary>
/// Maps the Streamable HTTP transport's "Mcp-Session-Id" header (sent on every request once a
/// session is established) to that session's <see cref="ProjectSessionHost"/>. Exists because an
/// HTTP MCP session spans many independent HTTP requests — unlike stdio, where one process is
/// one session and a singleton suffices — so per-session state has to be looked up explicitly
/// rather than falling out of ASP.NET Core's per-request DI scope. Verified empirically (not just
/// by reading the SDK's docs) against a local probe: the same host instance is reused across
/// separate HTTP requests in one session, distinct sessions get distinct hosts, and
/// <c>IHttpContextAccessor</c> sees the live per-request context from inside the factory.
///
/// The registry is a process-wide singleton; each entry — and the session's <c>ProjectId</c>/lock
/// via checkout — is established on the first tool call of that session. The bearer token forwarded
/// downstream is *not* captured once at that point: <see cref="GetOrCreate"/> hands the session's
/// <see cref="RemoteConnection"/> a closure over the live <see cref="IHttpContextAccessor"/>, so
/// every outbound call to the Project27 server (<see cref="RemoteProjectSession"/>'s `SendAsync`)
/// re-reads whatever bearer token the caller's *current* request presents — a mid-session token
/// refresh is forwarded on the very next call, not stuck on the token the session started with.
///
/// The transport exposes no "session closed" hook to application code, so entries aren't removed
/// when a client disconnects cleanly. Left unbounded would leak an open checkout *and* a live
/// <c>HttpClient</c> per abandoned session, so <see cref="PruneIdleAsync"/> — driven by
/// <see cref="McpIdleSessionSweeper"/> — evicts and disposes entries idle past <paramref
/// name="idleAfter"/>; an evicted entry's still-open server-side checkout lock is then recovered
/// the same way any other abandoned checkout is, via the server's own stale-lock timeout
/// (Locking:StaleAfterMinutes, E19).
/// </summary>
public sealed class McpSessionRegistry
{
    private const string SessionIdHeader = "Mcp-Session-Id";

    private readonly ConcurrentDictionary<string, Entry> _sessions = new();
    private readonly TimeProvider _timeProvider;

    public McpSessionRegistry(TimeProvider? timeProvider = null) => _timeProvider = timeProvider ?? TimeProvider.System;

    public ProjectSessionHost GetOrCreate(IHttpContextAccessor httpContextAccessor, string serverUrl)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new ProjectSessionException("no HTTP request context is available for this MCP session");
        var sessionId = httpContext.Request.Headers[SessionIdHeader].ToString();

        // Fail fast if this call has no bearer token at all — but the closure handed to
        // RemoteConnection below re-derives the token from httpContextAccessor on every future
        // outbound call, live, rather than reusing whatever RequireBearerToken returns here.
        RequireBearerToken(httpContext);
        Func<string?> tokenProvider = () => ExtractBearerToken(httpContextAccessor.HttpContext);

        if (string.IsNullOrEmpty(sessionId))
        {
            // No session id yet: the request that establishes a session (MCP "initialize") doesn't
            // carry one back to itself. A fresh, unregistered host is fine here — it isn't reused,
            // since every later request in this session does carry the header once initialize
            // returns it.
            return new ProjectSessionHost(new RemoteConnection(serverUrl, tokenProvider, DevUser: null));
        }

        var entry = _sessions.AddOrUpdate(
            sessionId,
            (_, state) => new Entry(new ProjectSessionHost(new RemoteConnection(state.ServerUrl, state.TokenProvider, DevUser: null)), state.Now),
            (_, existing, state) => existing with { LastTouchedUtc = state.Now },
            (ServerUrl: serverUrl, TokenProvider: tokenProvider, Now: _timeProvider.GetUtcNow()));
        return entry.Host;
    }

    /// <summary>Disposes and removes every session untouched for longer than <paramref name="idleAfter"/>.</summary>
    public async Task PruneIdleAsync(TimeSpan idleAfter, CancellationToken cancellationToken)
    {
        var cutoff = _timeProvider.GetUtcNow() - idleAfter;
        foreach (var (sessionId, entry) in _sessions)
        {
            if (entry.LastTouchedUtc > cutoff)
            {
                continue;
            }

            if (_sessions.TryRemove(new KeyValuePair<string, Entry>(sessionId, entry)))
            {
                await entry.Host.DisposeAsync().ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static void RequireBearerToken(HttpContext httpContext)
    {
        if (ExtractBearerToken(httpContext) is null)
        {
            throw new ProjectSessionException("authentication required; no bearer token on this connection");
        }
    }

    /// <summary>Internal (not private) so tests can verify it re-reads a live, mutated
    /// <see cref="HttpContext"/> instead of a value captured once — the exact property the
    /// token-refresh fix depends on (docs/spec/14-mcp-server.md "HTTP transport").</summary>
    internal static string? ExtractBearerToken(HttpContext? httpContext)
    {
        var header = httpContext?.Request.Headers.Authorization.ToString();
        return header is { Length: > 0 } && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : null;
    }

    private sealed record Entry(ProjectSessionHost Host, DateTimeOffset LastTouchedUtc);
}
