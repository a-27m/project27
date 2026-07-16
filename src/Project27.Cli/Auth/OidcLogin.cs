using System.Net;
using System.Text.Json;
using IdentityModel.Client;

namespace Project27.Cli.Auth;

internal sealed record LoginResult(string AccessToken, string? IdToken, string? RefreshToken, DateTimeOffset ExpiresAt);

internal enum BrowserResultCode
{
    Success,
    UserCancel,
    Timeout,
    UnknownError,
}

internal sealed record BrowserResult
{
    internal BrowserResultCode ResultCode { get; init; } = BrowserResultCode.UnknownError;
    internal string? Error { get; init; }
}

internal sealed record BrowserOptions
{
    internal string? Url { get; init; }
    internal TimeSpan Timeout { get; init; }
}

internal interface IBrowser
{
    internal Task<BrowserResult> InvokeAsync(BrowserOptions options, CancellationToken cancellationToken = default);
}

internal sealed class LoopbackPortNotAvailableException : Exception
{
    internal LoopbackPortNotAvailableException(int port) : base($"loopback port {port} is not available; falling back to device-code flow") { }
}

internal sealed class OidcLogin
{
    private static class OAuthState
    {
        internal static string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            return new string(Enumerable.Range(0, length)
                .Select(_ => chars[System.Security.Cryptography.RandomNumberGenerator.GetInt32(chars.Length)])
                .ToArray());
        }

        internal static (string CodeChallenge, string CodeVerifier) GeneratePkce()
        {
            var bytes = new byte[32];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            var verifier = Base64Encode(bytes);
            var challenge = Base64Encode(SHA256(System.Text.Encoding.ASCII.GetBytes(verifier)));
            return (challenge, verifier);
        }

        internal static string Base64Encode(byte[] buffer)
        {
            return System.Convert.ToBase64String(buffer)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        internal static byte[] SHA256(byte[] buffer)
        {
            return System.Security.Cryptography.SHA256.HashData(buffer);
        }
    }

    private readonly string _authority;
    private readonly string _clientId;
    private readonly string _scopes;
    private readonly int _loopbackPort;
    private readonly IBrowser? _browser;
    private readonly HttpMessageHandler? _backchannel;
    private (string? AuthorizationEndpoint, string? TokenEndpoint)? _discoveryCache;

    public OidcLogin(string authority, string clientId, string scopes, int loopbackPort, IBrowser? browser = null, HttpMessageHandler? backchannel = null)
    {
        _authority = authority;
        _clientId = clientId;
        _scopes = scopes;
        _loopbackPort = loopbackPort;
        _browser = browser;
        _backchannel = backchannel;
    }

    internal LoginResult LoginInteractive(TextWriter console)
    {
        LoopbackPortListener portListener;
        try
        {
            portListener = new LoopbackPortListener(_loopbackPort);
        }
        catch (IOException)
        {
            throw new LoopbackPortNotAvailableException(_loopbackPort);
        }

        var redirectUri = $"http://127.0.0.1:{portListener.Port}/callback";

        var (codeChallenge, codeVerifier) = OAuthState.GeneratePkce();
        var state = OAuthState.RandomString(32);
        var nonce = OAuthState.RandomString(32);

        console.WriteLine($"Discovering OIDC endpoints from {_authority}");
        var authorizeUrl = BuildAuthorizeUrl(redirectUri, codeChallenge, state, nonce).GetAwaiter().GetResult();
        console.WriteLine($"Opening browser to {authorizeUrl.Split('?')[0]}...");

        var browser = _browser ?? new SystemBrowser();
        var browserResult = browser.InvokeAsync(new BrowserOptions { Url = authorizeUrl, Timeout = TimeSpan.FromMinutes(5) }).GetAwaiter().GetResult();
        if (browserResult.ResultCode != BrowserResultCode.Success)
        {
            portListener.Close();
            if (browserResult.ResultCode == BrowserResultCode.Timeout)
            {
                throw new CliException("login timeout: you didn't complete authentication in time");
            }

            throw new CliException($"browser login failed: {browserResult.ResultCode}");
        }

        string? callbackUrl = null;
        try
        {
            callbackUrl = portListener.WaitForCallback(TimeSpan.FromMinutes(5));
        }
        finally
        {
            portListener.Close();
        }

        if (callbackUrl == null)
        {
            throw new CliException("login timeout: callback never received");
        }

        return ExchangeCodeForToken(callbackUrl, redirectUri, codeVerifier, state, nonce);
    }

    internal LoginResult LoginDeviceCode(TextWriter console)
    {
        using var client = new HttpClient(_backchannel ?? new HttpClientHandler());
        client.BaseAddress = new Uri(_authority.TrimEnd('/'));

        DeviceAuthorizationResponse deviceAuthResponse;
        try
        {
            deviceAuthResponse = client.RequestDeviceAuthorizationAsync(new DeviceAuthorizationRequest
            {
                Address = $"{_authority.TrimEnd('/')}/.well-known/openid-configuration",
                ClientId = _clientId,
                Scope = _scopes,
            }).GetAwaiter().GetResult();

            if (deviceAuthResponse.IsError)
            {
                throw new CliException($"device authorization failed: {deviceAuthResponse.Error}: {deviceAuthResponse.ErrorDescription}");
            }
        }
        catch (CliException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CliException($"failed to request device code: {ex.Message}", ex);
        }

        console.WriteLine($"\nPlease open this URL in your browser:\n  {deviceAuthResponse.VerificationUriComplete}\n");
        console.WriteLine($"Or visit {deviceAuthResponse.VerificationUri} and enter code: {deviceAuthResponse.UserCode}\n");

        var expiresSeconds = deviceAuthResponse.ExpiresIn ?? 600;
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresSeconds);
        var intervalSeconds = (double)(deviceAuthResponse.Interval > 0 ? deviceAuthResponse.Interval : 5);

        while (DateTimeOffset.UtcNow < expiresAt)
        {
            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(intervalSeconds));

            TokenResponse tokenResponse;
            try
            {
                tokenResponse = client.RequestDeviceTokenAsync(new DeviceTokenRequest
                {
                    Address = $"{_authority.TrimEnd('/')}/.well-known/openid-configuration",
                    ClientId = _clientId,
                    DeviceCode = deviceAuthResponse.DeviceCode ?? throw new InvalidOperationException("missing device code"),
                }).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                throw new CliException($"device token request failed: {ex.Message}", ex);
            }

            if (tokenResponse.IsError)
            {
                if (tokenResponse.Error == "authorization_pending" || tokenResponse.Error == "slow_down")
                {
                    if (tokenResponse.Error == "slow_down")
                    {
                        intervalSeconds *= 1.5;
                    }

                    continue;
                }

                throw new CliException($"device token error: {tokenResponse.Error}: {tokenResponse.ErrorDescription}");
            }

            string? idToken = null;
            try
            {
                idToken = tokenResponse.Json?.GetProperty("id_token").GetString();
            }
            catch
            {
                // id_token is optional
            }

            var tokenExpiresSeconds = tokenResponse.ExpiresIn > 0 ? (double)tokenResponse.ExpiresIn : 3600.0;
            var tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenExpiresSeconds);
            return new LoginResult(
                tokenResponse.AccessToken ?? throw new CliException("no access token returned"),
                idToken,
                tokenResponse.RefreshToken,
                tokenExpiresAt
            );
        }

        throw new CliException("device code expired before authentication completed");
    }

    internal LoginResult Refresh(StoredCredential stored)
    {
        using var client = new HttpClient(_backchannel ?? new HttpClientHandler());
        client.BaseAddress = new Uri(stored.Authority.TrimEnd('/'));

        TokenResponse tokenResponse;
        try
        {
            tokenResponse = client.RequestRefreshTokenAsync(new RefreshTokenRequest
            {
                Address = $"{stored.Authority.TrimEnd('/')}/.well-known/openid-configuration",
                ClientId = stored.CliClientId,
                RefreshToken = stored.RefreshToken ?? throw new CliException("no refresh token available"),
            }).GetAwaiter().GetResult();
        }
        catch (CliException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CliException($"token refresh failed: {ex.Message}", ex);
        }

        if (tokenResponse.IsError)
        {
            throw new CliException($"token refresh failed: {tokenResponse.Error}: {tokenResponse.ErrorDescription}");
        }

        var expiresSeconds = tokenResponse.ExpiresIn > 0 ? (double)tokenResponse.ExpiresIn : 3600.0;
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresSeconds);
        string? idToken = null;
        try
        {
            idToken = tokenResponse.Json?.GetProperty("id_token").GetString();
        }
        catch
        {
            // id_token is optional
        }

        return new LoginResult(
            tokenResponse.AccessToken ?? throw new CliException("no access token returned"),
            idToken,
            tokenResponse.RefreshToken,
            expiresAt
        );
    }

    private async Task<(string? AuthorizationEndpoint, string? TokenEndpoint)> FetchDiscoveryDocument()
    {
        if (_discoveryCache is { } cached)
        {
            return cached;
        }

        using var client = new HttpClient(_backchannel ?? new HttpClientHandler());
        var discoveryUrl = $"{_authority.TrimEnd('/')}/.well-known/openid-configuration";

        string? authorizationEndpoint = null;
        string? tokenEndpoint = null;
        try
        {
            var response = await client.GetAsync(discoveryUrl);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("authorization_endpoint", out var endpoint))
                {
                    authorizationEndpoint = endpoint.GetString();
                }

                if (doc.RootElement.TryGetProperty("token_endpoint", out var tokenEp))
                {
                    tokenEndpoint = tokenEp.GetString();
                }
            }
        }
        catch
        {
            // Fall back to hardcoded endpoint / surface the missing token endpoint to the caller
        }

        var result = (authorizationEndpoint, tokenEndpoint);
        _discoveryCache = result;
        return result;
    }

    private async Task<string> BuildAuthorizeUrl(string redirectUri, string codeChallenge, string state, string nonce)
    {
        var (discoveredEndpoint, _) = await FetchDiscoveryDocument();
        var authorizationEndpoint = string.IsNullOrEmpty(discoveredEndpoint)
            ? $"{_authority.TrimEnd('/')}/authorize"
            : discoveredEndpoint;

        var authorizeUrl = $"{authorizationEndpoint}?" +
            $"response_type=code&" +
            $"client_id={Uri.EscapeDataString(_clientId)}&" +
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
            $"scope={Uri.EscapeDataString(_scopes)}&" +
            $"code_challenge={Uri.EscapeDataString(codeChallenge)}&" +
            $"code_challenge_method=S256&" +
            $"state={Uri.EscapeDataString(state)}&" +
            $"nonce={Uri.EscapeDataString(nonce)}";
        return authorizeUrl;
    }

    private LoginResult ExchangeCodeForToken(string callbackUrl, string redirectUri, string codeVerifier, string expectedState, string expectedNonce)
    {
        // callbackUrl from loopback listener is just the raw request path+query (e.g., "/?code=...&state=...")
        // Construct a full URI to parse it properly
        var fullCallbackUrl = redirectUri.TrimEnd('/') + callbackUrl;
        var uri = new Uri(fullCallbackUrl);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var code = query.Get("code");
        var state = query.Get("state");
        var error = query.Get("error");
        var errorDescription = query.Get("error_description");

        if (!string.IsNullOrEmpty(error))
        {
            throw new CliException($"OIDC error: {error}: {errorDescription}");
        }

        if (string.IsNullOrEmpty(code))
        {
            throw new CliException("no authorization code in callback");
        }

        if (state != expectedState)
        {
            throw new CliException("state mismatch in callback");
        }

        using var client = new HttpClient(_backchannel ?? new HttpClientHandler());

        var (_, tokenEndpoint) = FetchDiscoveryDocument().GetAwaiter().GetResult();

        if (string.IsNullOrEmpty(tokenEndpoint))
        {
            throw new CliException($"Failed to discover token endpoint from {_authority}/.well-known/openid-configuration. Verify the Authority URL is correct.");
        }

        try
        {
            // Build token request body manually to ensure client_id is included (required by Azure AD)
            var tokenRequestBody = new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "code", code },
                { "redirect_uri", redirectUri },
                { "client_id", _clientId },
                { "code_verifier", codeVerifier },
            };

            var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
            {
                Content = new FormUrlEncodedContent(tokenRequestBody),
            };

            var response = client.SendAsync(request).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                throw new CliException($"Token endpoint returned {response.StatusCode}: {errorBody}");
            }

            var tokenJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var tokenDoc = JsonDocument.Parse(tokenJson);
            var root = tokenDoc.RootElement;

            if (root.TryGetProperty("error", out var errorProp))
            {
                var tokenError = errorProp.GetString();
                var tokenErrorDesc = root.TryGetProperty("error_description", out var descProp) ? descProp.GetString() : "";
                throw new CliException($"Token exchange error: {tokenError} - {tokenErrorDesc}");
            }

            var accessToken = root.GetProperty("access_token").GetString();
            if (string.IsNullOrEmpty(accessToken))
            {
                throw new CliException("No access_token in response");
            }

            root.TryGetProperty("id_token", out var idTokenElement);
            var idToken = idTokenElement.ValueKind != JsonValueKind.Undefined ? idTokenElement.GetString() : null;

            if (idToken != null && ExtractNonceClaim(idToken) is { } actualNonce && actualNonce != expectedNonce)
            {
                throw new CliException("nonce mismatch in id_token; the login response does not match this request");
            }

            root.TryGetProperty("refresh_token", out var refreshTokenElement);
            var refreshToken = refreshTokenElement.ValueKind != JsonValueKind.Undefined ? refreshTokenElement.GetString() : null;

            var expiresIn = root.TryGetProperty("expires_in", out var expiresElement) ? expiresElement.GetInt32() : 3600;
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);

            return new LoginResult(accessToken, idToken, refreshToken, expiresAt);
        }
        catch (CliException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CliException($"token exchange failed: {ex.Message}", ex);
        }
    }

    private static string? ExtractNonceClaim(string idToken)
    {
        try
        {
            var parts = idToken.Split('.');
            if (parts.Length < 2)
            {
                return null;
            }

            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload += new string('=', (4 - payload.Length % 4) % 4);
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("nonce", out var nonce) ? nonce.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private sealed class LoopbackPortListener : IDisposable
    {
        private readonly HttpListener? _listener;
        private readonly int _port;
        private string? _callbackUrl;

        internal int Port => _port;

        internal LoopbackPortListener(int port)
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                _listener.Start();
            }
            catch (HttpListenerException ex)
            {
                _listener?.Close();
                throw new IOException($"Port {port} is already in use", ex);
            }

            _port = port;
        }

        internal string? WaitForCallback(TimeSpan timeout)
        {
            if (_listener == null)
            {
                return null;
            }

            _listener.BeginGetContext(ar =>
            {
                try
                {
                    var ctx = _listener.EndGetContext(ar);
                    _callbackUrl = ctx.Request.RawUrl;

                    using (var response = ctx.Response)
                    {
                        response.StatusCode = 200;
                        response.ContentType = "text/html";
                        var message = "<html><body><h1>You may close this window.</h1></body></html>";
                        var buffer = System.Text.Encoding.UTF8.GetBytes(message);
                        response.ContentLength64 = buffer.Length;
                        response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                }
                catch
                {
                    // Ignore any errors during callback handling
                }
            }, null);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (_callbackUrl == null && sw.Elapsed < timeout)
            {
                System.Threading.Thread.Sleep(100);
            }

            sw.Stop();
            return _callbackUrl;
        }

        internal void Close() => _listener?.Close();

        public void Dispose()
        {
            _listener?.Close();
            (_listener as IDisposable)?.Dispose();
        }
    }

    private sealed class SystemBrowser : IBrowser
    {
        public async Task<BrowserResult> InvokeAsync(BrowserOptions options, CancellationToken cancellationToken = default)
        {
            try
            {
                var url = options.Url ?? string.Empty;
                var psi = new System.Diagnostics.ProcessStartInfo(url)
                {
                    UseShellExecute = true,
                    CreateNoWindow = true,
                };

                System.Diagnostics.Process.Start(psi);
                return new BrowserResult { ResultCode = BrowserResultCode.Success };
            }
            catch (Exception ex)
            {
                return new BrowserResult { ResultCode = BrowserResultCode.UnknownError, Error = ex.Message };
            }
        }
    }
}
