using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.AspNetCore.HttpOverrides;

namespace Soso.Api;

public static class TunnelProxy
{
    public static void Configure(ForwardedHeadersOptions options, IConfiguration configuration)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        options.KnownProxies.Add(IPAddress.Loopback);
        options.KnownProxies.Add(IPAddress.IPv6Loopback);

        var cloudflareTunnel = configuration.GetValue<bool>("CloudflareTunnel");
        var configuredProxy = configuration["TrustedProxy"];
        var hasConfiguredProxy = !string.IsNullOrWhiteSpace(configuredProxy);
        if (hasConfiguredProxy)
        {
            options.KnownProxies.Add(IPAddress.Parse(configuredProxy!));
        }
        else if (cloudflareTunnel)
        {
            var gateways = NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(network => network.GetIPProperties().GatewayAddresses)
                .Select(gateway => gateway.Address)
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .Where(address => !address.Equals(IPAddress.Any))
                .Distinct()
                .ToArray();
            if (gateways.Length != 1)
            {
                throw new InvalidOperationException("Set TrustedProxy to the exact tunnel peer address when no unique Docker IPv4 gateway is available.");
            }
            options.KnownProxies.Add(gateways[0]);
        }

        if (cloudflareTunnel)
        {
            options.ForwardedForHeaderName = "CF-Connecting-IP";
        }
    }
}