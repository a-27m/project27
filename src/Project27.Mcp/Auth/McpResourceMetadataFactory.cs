using ModelContextProtocol.Authentication;

namespace Project27.Mcp.Auth;

/// <summary>
/// Pure helpers behind the HTTP transport's RFC 9728 protected-resource metadata (see
/// "OAuth discovery" in docs/spec/14-mcp-server.md). Split out from Program.cs's top-level
/// statements so they're unit-testable — local functions in a top-level Program.cs compile to
/// private members the test assembly can't reach even with InternalsVisibleTo.
/// </summary>
internal static class McpResourceMetadataFactory
{
    /// <summary>
    /// The audiences a bearer token must carry one of to be accepted: the pre-existing
    /// `Auth:Audience` (shared with Project27.Server, unaffected clients keep working) plus the
    /// RFC 9728 `resource` value, if set — Entra (and RFC 8707 resource-indicator clients
    /// generally) mint tokens with `aud` = the resource a client requested, which is this MCP
    /// server's own external URL, not the older API audience.
    /// </summary>
    internal static IReadOnlyList<string> ValidAudiences(string? audience, string? resource) =>
        new[] { audience, resource }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray()!;

    internal static ProtectedResourceMetadata BuildResourceMetadata(string resource, string authority, IReadOnlyList<string> scopes)
    {
        // BearerMethodsSupported defaults to ["header"] already (the only method this SDK/we
        // support), so it's left untouched here rather than re-added.
        var metadata = new ProtectedResourceMetadata
        {
            Resource = resource,
            AuthorizationServers = { authority },
        };

        foreach (var scope in scopes)
        {
            metadata.ScopesSupported.Add(scope);
        }

        return metadata;
    }

    /// <summary>
    /// RFC 9728 §3.1 path-insertion: the metadata document for a resource at
    /// "https://host/prefix" lives at "https://host/.well-known/oauth-protected-resource/prefix".
    /// Computed explicitly from the configured external `resource` URL rather than the incoming
    /// request's host/path, because a path-prefixed proxy in front of this process (Mcp:PathPrefix)
    /// means the request Kestrel sees is not reliably the externally-visible address.
    /// </summary>
    internal static Uri ResourceMetadataUri(string resource)
    {
        if (!Uri.TryCreate(resource, UriKind.Absolute, out var resourceUri))
        {
            throw new InvalidOperationException(
                $"Auth:Resource ('{resource}') is not an absolute URL; it must be this deployment's " +
                "externally-visible MCP address, e.g. https://mcp.example.com/mcp/ (docs/spec/14-mcp-server.md " +
                "\"OAuth discovery\").");
        }

        var insertedPath = "/.well-known/oauth-protected-resource" + resourceUri.AbsolutePath.TrimEnd('/');
        return new Uri(resourceUri, insertedPath);
    }
}
