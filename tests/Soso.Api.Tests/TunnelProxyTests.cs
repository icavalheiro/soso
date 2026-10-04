using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Soso.Api;
using Xunit;

namespace Soso.Api.Tests;

public sealed class TunnelProxyTests
{
    [Theory]
    [InlineData("192.0.2.10", "https", true, "203.0.113.7")]
    [InlineData("192.0.2.10", "http", false, "203.0.113.7")]
    [InlineData("192.0.2.11", "https", false, "192.0.2.11")]
    public async Task OnlyTrustedTunnelPeersCanForwardSchemeAndClientIp(string peer, string scheme, bool expectedHttps, string expectedIp)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CloudflareTunnel"] = "true",
            ["TrustedProxy"] = "192.0.2.10"
        }).Build();
        var options = new ForwardedHeadersOptions();
        TunnelProxy.Configure(options, configuration);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            await next();
        });
        app.UseForwardedHeaders(options);
        app.Run(context => context.Response.WriteAsJsonAsync(new TunnelResponse(context.Request.IsHttps, context.Connection.RemoteIpAddress!.ToString())));
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", scheme);
        client.DefaultRequestHeaders.Add("CF-Connecting-IP", "203.0.113.7");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.99");

        var result = await client.GetFromJsonAsync<TunnelResponse>("/");
        Assert.NotNull(result);
        Assert.Equal(expectedHttps, result.Https);
        Assert.Equal(expectedIp, result.ClientIp);
        Assert.Empty(options.KnownIPNetworks);
        Assert.Equal(1, options.ForwardLimit);
    }

    [Fact]
    public void NonTunnelConfigurationDoesNotTrustArbitraryNetworks()
    {
        var options = new ForwardedHeadersOptions();
        TunnelProxy.Configure(options, new ConfigurationBuilder().Build());

        Assert.Empty(options.KnownIPNetworks);
        Assert.Equal(2, options.KnownProxies.Count);
        Assert.Contains(IPAddress.Loopback, options.KnownProxies);
        Assert.Contains(IPAddress.IPv6Loopback, options.KnownProxies);
        Assert.Equal("X-Forwarded-For", options.ForwardedForHeaderName);
    }

    private sealed record TunnelResponse(bool Https, string ClientIp);
}