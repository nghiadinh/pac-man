using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;

namespace MatchServer.IntegrationTests;

/// <summary>
/// Boots the real app in memory and hands out hub connections wired through the test server.
/// </summary>
/// <remarks>
/// research.md §4: unit tests prove a rule is right in isolation, but only this layer proves the
/// hub actually calls it - e.g. that a disconnect really travels the FR-020 forfeit path through
/// the genuine SignalR lifecycle rather than just through a rule class in isolation.
/// </remarks>
public sealed class MatchServerFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        return base.CreateHost(builder);
    }

    /// <summary>Opens a hub connection that talks to the in-memory test server.</summary>
    public HubConnection CreateHubConnection()
    {
        var handler = Server.CreateHandler();

        return new HubConnectionBuilder()
            .WithUrl(new Uri(Server.BaseAddress, "/hubs/match"), options =>
            {
                options.HttpMessageHandlerFactory = _ => handler;
                options.WebSocketFactory = null; // long-polling over the test handler
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            })
            .Build();
    }
}

/// <summary>Shape of the JoinMatch response (contract connection lifecycle).</summary>
public sealed record JoinResult(string MatchId, string Role, string Status, bool Started);
