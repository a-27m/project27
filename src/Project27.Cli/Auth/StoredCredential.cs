namespace Project27.Cli.Auth;

internal sealed record StoredCredential(
    string ServerUrl,
    string Authority,
    string CliClientId,
    string Scopes,
    string AccessToken,
    string? IdToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAt);
