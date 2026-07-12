using System.Net;
using Xunit;

namespace Project27.Server.Tests;

[Collection("server")]
public sealed class HealthzTests
{
    private readonly ServerFixture _server;

    public HealthzTests(ServerFixture server) => _server = server;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Healthz_is_reachable_without_auth()
    {
        var anonymous = _server.Client(user: null);
        var response = await anonymous.GetAsync("/healthz", Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
