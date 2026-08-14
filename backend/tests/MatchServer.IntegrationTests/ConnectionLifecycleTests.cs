using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace MatchServer.IntegrationTests;

/// <summary>
/// Phase 2 checkpoint: two clients connect, are assigned opposing roles, and receive ticking
/// authoritative state. Everything in User Story 1 builds on this holding.
/// </summary>
/// <remarks>
/// Each test gets its OWN server. MatchManager is a singleton holding every live match, and
/// JoinMatch attaches to any match still waiting for a second player - so a shared fixture lets a
/// half-open match leak from one test into the next and silently changes which match a client
/// joins. Booting per test costs a second and buys real isolation.
/// </remarks>
public sealed class ConnectionLifecycleTests : IDisposable
{
    private readonly MatchServerFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task First_joiner_is_runner_and_second_is_hunter_in_the_same_match()
    {
        await using var runner = _factory.CreateHubConnection();
        await using var hunter = _factory.CreateHubConnection();

        await runner.StartAsync();
        var runnerJoin = await runner.InvokeAsync<JoinResult>("JoinMatch", (string?)null);

        Assert.Equal("Runner", runnerJoin.Role);
        Assert.False(runnerJoin.Started); // timer waits for the second player

        await hunter.StartAsync();
        var hunterJoin = await hunter.InvokeAsync<JoinResult>("JoinMatch", (string?)null);

        Assert.Equal("Hunter", hunterJoin.Role);
        Assert.True(hunterJoin.Started);
        Assert.Equal(runnerJoin.MatchId, hunterJoin.MatchId);
    }

    [Fact]
    public async Task Both_clients_receive_ticking_state_once_the_match_is_active()
    {
        await using var runner = _factory.CreateHubConnection();
        await using var hunter = _factory.CreateHubConnection();

        var runnerStates = new List<JsonElement>();
        var hunterStates = new List<JsonElement>();
        runner.On<JsonElement>("StateUpdate", s => runnerStates.Add(s));
        hunter.On<JsonElement>("StateUpdate", s => hunterStates.Add(s));

        await runner.StartAsync();
        await runner.InvokeAsync<JoinResult>("JoinMatch", (string?)null);
        await hunter.StartAsync();
        await hunter.InvokeAsync<JoinResult>("JoinMatch", (string?)null);

        // State is broadcast from the moment a player joins, so the earliest snapshots are still
        // WaitingForPlayers - that is what drives the lobby screen. Only the active ones matter here.
        static List<JsonElement> ActiveOnly(List<JsonElement> states) => states
            .Where(s => s.GetProperty("status").GetString() == "Active")
            .ToList();

        // Wait for the condition rather than sleeping a fixed 500ms: every test class boots its own
        // server and xUnit runs them in parallel, so under load a fixed delay can capture a single
        // active tick and make the "clock advanced" assertion fail for no real reason.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline &&
               (ActiveOnly(runnerStates).Count < 2 || ActiveOnly(hunterStates).Count < 1))
        {
            await Task.Delay(50);
        }

        Assert.NotEmpty(runnerStates);
        Assert.NotEmpty(hunterStates);

        var activeStates = ActiveOnly(runnerStates);

        Assert.True(
            activeStates.Count >= 2,
            $"expected at least 2 active ticks within 10s, got {activeStates.Count}");
        Assert.Contains(hunterStates, s => s.GetProperty("status").GetString() == "Active");

        // Self-consistent rather than a hardcoded total, so editing the maze does not break this
        // test for reasons unrelated to what it is actually checking.
        var map = activeStates[0].GetProperty("map");
        var pelletCount = map.GetProperty("pellets").GetArrayLength();
        var powerCount = map.GetProperty("powerPellets").GetArrayLength();

        Assert.Equal(pelletCount + powerCount, map.GetProperty("totalPelletCount").GetInt32());
        Assert.Equal(4, powerCount); // one power pellet per corner - what makes FR-012 camping matter
        Assert.True(pelletCount > 100, $"maze has only {pelletCount} pellets; expected a full board");

        // The clock must actually be advancing, not merely present.
        var elapsedValues = activeStates.Select(s => s.GetProperty("elapsedMs").GetInt64()).ToList();
        Assert.True(
            elapsedValues[^1] > elapsedValues[0],
            $"elapsedMs did not advance across {elapsedValues.Count} active ticks");
    }

    [Fact]
    public async Task Invalid_input_is_rejected_rather_than_clamped()
    {
        await using var runner = _factory.CreateHubConnection();
        await using var hunter = _factory.CreateHubConnection();

        await runner.StartAsync();
        await runner.InvokeAsync<JoinResult>("JoinMatch", (string?)null);
        await hunter.StartAsync();
        await hunter.InvokeAsync<JoinResult>("JoinMatch", (string?)null);

        // Constitution Fair-Play: out-of-range input must be refused outright, never coerced
        // into the nearest legal value.
        await Assert.ThrowsAsync<HubException>(
            () => runner.InvokeAsync("SendInput", "Diagonal"));

        // Numeric aliases are refused too. Enum.TryParse would happily turn "0" into None, which
        // would let a client bypass the literal set the contract defines.
        await Assert.ThrowsAsync<HubException>(() => runner.InvokeAsync("SendInput", "0"));
        await Assert.ThrowsAsync<HubException>(() => runner.InvokeAsync("SendInput", "2"));

        // Case matters - the contract specifies exact literals.
        await Assert.ThrowsAsync<HubException>(() => runner.InvokeAsync("SendInput", "up"));

        // A legal direction still works afterwards - the rejection is per-message, not fatal.
        await runner.InvokeAsync("SendInput", "Up");
    }
}
