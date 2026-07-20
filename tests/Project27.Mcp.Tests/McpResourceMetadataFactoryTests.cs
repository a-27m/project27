using Project27.Mcp.Auth;
using Xunit;

namespace Project27.Mcp.Tests;

public sealed class McpResourceMetadataFactoryTests
{
    [Fact]
    public void ValidAudiences_IncludesBothAudienceAndResource()
    {
        var result = McpResourceMetadataFactory.ValidAudiences("api://project27", "https://mcp.example.com/mcp/");

        Assert.Equal(["api://project27", "https://mcp.example.com/mcp/"], result);
    }

    [Fact]
    public void ValidAudiences_DropsNullOrEmptyEntries()
    {
        var result = McpResourceMetadataFactory.ValidAudiences(null, "https://mcp.example.com/mcp/");

        Assert.Equal(["https://mcp.example.com/mcp/"], result);
    }

    [Fact]
    public void ValidAudiences_DeduplicatesWhenAudienceAndResourceAreEqual()
    {
        var result = McpResourceMetadataFactory.ValidAudiences("https://mcp.example.com/mcp/", "https://mcp.example.com/mcp/");

        Assert.Equal(["https://mcp.example.com/mcp/"], result);
    }

    [Fact]
    public void BuildResourceMetadata_SetsResourceAuthorityAndScopes()
    {
        var metadata = McpResourceMetadataFactory.BuildResourceMetadata(
            "https://mcp.example.com/mcp/",
            "https://login.microsoftonline.com/tenant/v2.0",
            ["openid", "offline_access"]);

        Assert.Equal("https://mcp.example.com/mcp/", metadata.Resource);
        Assert.Equal(["https://login.microsoftonline.com/tenant/v2.0"], metadata.AuthorizationServers);
        Assert.Equal(["header"], metadata.BearerMethodsSupported);
        Assert.Equal(["openid", "offline_access"], metadata.ScopesSupported);
    }

    [Theory]
    [InlineData("https://mcp.example.com/mcp/", "https://mcp.example.com/.well-known/oauth-protected-resource/mcp")]
    [InlineData("https://mcp.example.com/mcp", "https://mcp.example.com/.well-known/oauth-protected-resource/mcp")]
    [InlineData("https://mcp.example.com/", "https://mcp.example.com/.well-known/oauth-protected-resource")]
    public void ResourceMetadataUri_InsertsWellKnownPathBeforeResourcePath(string resource, string expected)
    {
        var uri = McpResourceMetadataFactory.ResourceMetadataUri(resource);

        Assert.Equal(expected, uri.ToString());
    }
}
