using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace Project27.Server.Auth;

/// <summary>
/// Development-only authentication (decision D5): the caller picks a user from the
/// configured list with the `X-Dev-User` header, receiving the same claims shape as
/// OIDC. Registration refuses non-Development environments.
/// </summary>
public sealed class DevAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "DevAuth";
    public const string HeaderName = "X-Dev-User";

    private readonly IConfiguration _configuration;

    public DevAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.TryGetValue(HeaderName, out var values) is false || values.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var user = values.ToString().Trim();
        var allowed = _configuration.GetSection("Auth:DevUsers").Get<string[]>() ?? ["alice", "bob", "carol"];
        if (!allowed.Contains(user, StringComparer.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.Fail($"'{user}' is not a configured dev user."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.ToLowerInvariant()), new Claim(ClaimTypes.Name, user)],
            SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public static class AuthSetup
{
    public const string SmartScheme = "Smart";

    /// <summary>
    /// OIDC bearer (when `Auth:Authority` is set) plus DevAuth (when `Auth:DevAuth`
    /// is true — Development only). A policy scheme routes per request by header.
    /// </summary>
    public static IServiceCollection AddProject27Auth(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var devAuth = configuration.GetValue<bool>("Auth:DevAuth");
        var authority = configuration["Auth:Authority"];
        if (devAuth && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "DevAuth is enabled but the environment is not Development; refusing to start (decision D5).");
        }

        if (!devAuth && string.IsNullOrEmpty(authority))
        {
            throw new InvalidOperationException(
                "No authentication configured: set Auth:Authority (OIDC) or Auth:DevAuth=true (Development).");
        }

        var builder = services
            .AddAuthentication(SmartScheme)
            .AddPolicyScheme(SmartScheme, "OIDC or DevAuth", options =>
                options.ForwardDefaultSelector = context =>
                    devAuth && (context.Request.Headers.ContainsKey(DevAuthHandler.HeaderName) || string.IsNullOrEmpty(authority))
                        ? DevAuthHandler.SchemeName
                        : JwtBearerDefaults.AuthenticationScheme);

        if (devAuth)
        {
            builder.AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevAuthHandler.SchemeName, null);
        }

        if (!string.IsNullOrEmpty(authority))
        {
            builder.AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = configuration["Auth:Audience"];
            });
        }

        services.AddAuthorization();
        return services;
    }
}
