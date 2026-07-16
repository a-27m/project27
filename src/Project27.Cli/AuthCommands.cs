using System.CommandLine;
using System.Text.Json;
using Project27.Cli.Auth;

namespace Project27.Cli;

internal static class AuthCommands
{
    public static Command Login()
    {
        var deviceCodeOpt = new Option<bool>("--device-code")
        {
            Description = "Use device-code flow instead of interactive browser login (for headless/SSH environments).",
        };

        var command = new Command("login", "Log in to a Project27 server via OIDC.") { deviceCodeOpt };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var serverUrl = context.ServerUrl ?? throw new CliException("login requires --server <url> or P27_SERVER");
            var useDeviceCode = parseResult.GetValue(deviceCodeOpt);

            using var anonClient = new RemoteClient(serverUrl, token: null, devUser: null);
            var config = anonClient.GetAuthConfig();

            if (config.DevAuth && string.IsNullOrEmpty(config.Authority))
            {
                throw new CliException("this server only supports --dev-user authentication; no OIDC provider is configured");
            }

            if (string.IsNullOrEmpty(config.Authority))
            {
                throw new CliException("this server has no OIDC provider configured");
            }

            var clientId = CliConfig.CliClientId ?? throw new CliException("no CliClientId configured; set CliClientId in the CLI's appsettings.json");
            var login = new OidcLogin(config.Authority, clientId, config.Scopes ?? "openid profile offline_access", CliConfig.CliLoopbackPort);

            LoginResult result;
            try
            {
                result = useDeviceCode
                    ? login.LoginDeviceCode(context.Out)
                    : login.LoginInteractive(context.Out);
            }
            catch (LoopbackPortNotAvailableException ex)
            {
                context.Out.WriteLine($"Note: {ex.Message}");
                context.Out.WriteLine();
                result = login.LoginDeviceCode(context.Out);
            }

            var credential = new StoredCredential(
                serverUrl,
                config.Authority,
                clientId,
                config.Scopes ?? "openid profile offline_access",
                result.AccessToken,
                result.IdToken,
                result.RefreshToken,
                result.ExpiresAt
            );

            CredentialStore.Put(credential);

            var displayName = ExtractDisplayName(result.IdToken);
            context.Report(
                new { serverUrl, loggedInAs = displayName, expiresAt = result.ExpiresAt },
                $"logged in to {serverUrl} as {displayName}"
            );

            return 0;
        }));

        return command;
    }

    public static Command Logout()
    {
        var command = new Command("logout", "Log out from a Project27 server (delete stored credentials).");
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var serverUrl = context.ServerUrl ?? throw new CliException("logout requires --server <url> or P27_SERVER");

            var credential = CredentialStore.Get(serverUrl);
            if (credential == null)
            {
                context.Report(new { message = "not logged in" }, $"not logged in to {serverUrl}");
                return 0;
            }

            CredentialStore.Remove(serverUrl);
            context.Report(new { message = "logged out" }, $"logged out from {serverUrl}");
            return 0;
        }));

        return command;
    }

    public static Command WhoAmI()
    {
        var command = new Command("whoami", "Display the current authenticated identity.");
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var explicit_token = parseResult.GetValue(CliRoot.TokenOption);
            var explicit_dev_user = parseResult.GetValue(CliRoot.DevUserOption);
            var serverUrl = context.ServerUrl;

            string identity;
            string mode;

            if (!string.IsNullOrEmpty(explicit_token))
            {
                identity = "using explicit --token (no display name available)";
                mode = "explicit_token";
            }
            else if (!string.IsNullOrEmpty(explicit_dev_user))
            {
                identity = explicit_dev_user;
                mode = "dev_user";
            }
            else if (!string.IsNullOrEmpty(serverUrl))
            {
                var credential = CredentialStore.Get(serverUrl);
                if (credential == null)
                {
                    throw new CliException($"not logged in to {serverUrl}; run: p27 login --server {serverUrl}");
                }

                identity = ExtractDisplayName(credential.IdToken) ?? "logged in (no display name available)";
                mode = "stored_credential";
            }
            else
            {
                throw new CliException("whoami requires --server <url> (or P27_SERVER), --token, or --dev-user");
            }

            context.Report(
                new { identity, mode, serverUrl },
                identity
            );

            return 0;
        }));

        return command;
    }

    private static string? ExtractDisplayName(string? idToken)
    {
        if (string.IsNullOrEmpty(idToken))
        {
            return null;
        }

        try
        {
            var parts = idToken.Split('.');
            if (parts.Length < 2)
            {
                return null;
            }

            var payload = parts[1];
            payload += new string('=', (4 - payload.Length % 4) % 4);
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("name", out var name))
            {
                return name.GetString();
            }

            if (doc.RootElement.TryGetProperty("email", out var email))
            {
                return email.GetString();
            }

            if (doc.RootElement.TryGetProperty("sub", out var sub))
            {
                return sub.GetString();
            }
        }
        catch
        {
            // Ignore JWT parsing errors; fall through to null
        }

        return null;
    }
}
