using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace MatchServer.IntegrationTests;

/// <summary>
/// Room joining through the real hub: auto-matching, playing a specific person by code, and the
/// errors a client actually receives when a code is wrong or the room is taken.
/// </summary>
public sealed class RoomCodeTests : IDisposable
{
    private readonly MatchServerFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Auto_matching_pairs_two_clients_into_one_room()
    {
        await using var runner = _factory.CreateHubConnection();
        await using var hunter = _factory.CreateHubConnection();

        await runner.StartAsync();
        var runnerJoin = await runner.InvokeAsync<JoinResult>("JoinMatch", (string?)null);

        await hunter.StartAsync();
        var hunterJoin = await hunter.InvokeAsync<JoinResult>("JoinMatch", (string?)null);

        Assert.Equal(runnerJoin.MatchId, hunterJoin.MatchId);
        Assert.Equal("Runner", runnerJoin.Role);
        Assert.Equal("Hunter", hunterJoin.Role);
    }

    [Fact]
    public async Task A_shared_code_puts_two_clients_in_the_same_room()
    {
        await using var first = _factory.CreateHubConnection();
        await using var second = _factory.CreateHubConnection();

        await first.StartAsync();
        var firstJoin = await first.InvokeAsync<JoinResult>("JoinMatch", "PLAY");

        await second.StartAsync();
        var secondJoin = await second.InvokeAsync<JoinResult>("JoinMatch", "PLAY");

        Assert.Equal("PLAY", firstJoin.MatchId);
        Assert.Equal("PLAY", secondJoin.MatchId);
        Assert.True(secondJoin.Started);
    }

    [Fact]
    public async Task The_room_code_is_returned_so_it_can_be_shared()
    {
        // Auto-matched rooms get a generated code too - that is what the waiting player shows
        // their friend to pull them in rather than waiting on a stranger.
        await using var runner = _factory.CreateHubConnection();

        await runner.StartAsync();
        var join = await runner.InvokeAsync<JoinResult>("JoinMatch", (string?)null);

        Assert.Equal(4, join.MatchId.Length);
        Assert.Matches("^[A-Z2-9]{4}$", join.MatchId);
    }

    [Fact]
    public async Task A_private_room_is_never_given_to_an_auto_matcher()
    {
        // The whole point of a code is that the slot is held for a specific person.
        await using var host = _factory.CreateHubConnection();
        await using var stranger = _factory.CreateHubConnection();

        await host.StartAsync();
        var hostJoin = await host.InvokeAsync<JoinResult>("JoinMatch", "MYNE");

        await stranger.StartAsync();
        var strangerJoin = await stranger.InvokeAsync<JoinResult>("JoinMatch", (string?)null);

        Assert.NotEqual(hostJoin.MatchId, strangerJoin.MatchId);
        Assert.Equal("Runner", strangerJoin.Role);
    }

    [Fact]
    public async Task Joining_a_full_room_reports_it_rather_than_rerouting()
    {
        await using var first = _factory.CreateHubConnection();
        await using var second = _factory.CreateHubConnection();
        await using var third = _factory.CreateHubConnection();

        await first.StartAsync();
        await first.InvokeAsync<JoinResult>("JoinMatch", "FULL");
        await second.StartAsync();
        await second.InvokeAsync<JoinResult>("JoinMatch", "FULL");

        await third.StartAsync();
        var error = await Assert.ThrowsAsync<HubException>(
            () => third.InvokeAsync<JoinResult>("JoinMatch", "FULL"));

        Assert.Contains("FULL", error.Message);
        Assert.Contains("two players", error.Message);
    }

    [Theory]
    [InlineData("ABC")]
    [InlineData("TOOLONG")]
    [InlineData("AB!Z")]
    [InlineData("AB0Z")]
    public async Task An_invalid_code_is_refused_with_a_usable_message(string code)
    {
        await using var client = _factory.CreateHubConnection();
        await client.StartAsync();

        var error = await Assert.ThrowsAsync<HubException>(
            () => client.InvokeAsync<JoinResult>("JoinMatch", code));

        // The message has to tell the player what a valid code looks like, not just "invalid".
        Assert.Contains("4 characters", error.Message);
    }

    [Fact]
    public async Task A_rejected_code_does_not_break_the_connection()
    {
        await using var client = _factory.CreateHubConnection();
        await client.StartAsync();

        await Assert.ThrowsAsync<HubException>(
            () => client.InvokeAsync<JoinResult>("JoinMatch", "!!!!"));

        // Retrying with a good code on the same connection must still work.
        var join = await client.InvokeAsync<JoinResult>("JoinMatch", "NEXT");
        Assert.Equal("NEXT", join.MatchId);
    }

    [Fact]
    public async Task Codes_are_case_insensitive_over_the_wire()
    {
        await using var first = _factory.CreateHubConnection();
        await using var second = _factory.CreateHubConnection();

        await first.StartAsync();
        var firstJoin = await first.InvokeAsync<JoinResult>("JoinMatch", "abcd");

        await second.StartAsync();
        var secondJoin = await second.InvokeAsync<JoinResult>("JoinMatch", "ABCD");

        Assert.Equal("ABCD", firstJoin.MatchId);
        Assert.Equal(firstJoin.MatchId, secondJoin.MatchId);
    }
}
