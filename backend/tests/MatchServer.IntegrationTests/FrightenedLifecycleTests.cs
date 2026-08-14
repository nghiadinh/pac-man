using System.Text.Json;
using MatchServer.Generated;
using MatchServer.State;
using Microsoft.AspNetCore.SignalR.Client;

namespace MatchServer.IntegrationTests;

/// <summary>
/// The full ghost lifecycle through the real hub:
/// Normal → Frightened → EyesOnly → Respawning → Normal (FR-005, FR-006, FR-009).
/// </summary>
public sealed class FrightenedLifecycleTests : IDisposable
{
    private readonly MatchServerFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    /// <summary>
    /// Ghost sub-state from the most recent StateUpdate that actually has a ghost in it.
    /// </summary>
    /// <remarks>
    /// Snapshots sent before the Hunter joined carry a null ghost, and long-polling can deliver
    /// one of those after the handler is attached - so the newest state is not reliably the newest
    /// state WITH a ghost.
    /// </remarks>
    private static string? ReadGhostSubState(List<JsonElement> states)
    {
        for (var i = states.Count - 1; i >= 0; i--)
        {
            var ghost = states[i].GetProperty("ghost");
            if (ghost.ValueKind == JsonValueKind.Object)
            {
                return ghost.GetProperty("ghostSubState").GetString();
            }
        }

        return null;
    }

    /// <summary>Latest ghost object, or null if none has arrived yet.</summary>
    private static JsonElement? ReadGhost(List<JsonElement> states)
    {
        for (var i = states.Count - 1; i >= 0; i--)
        {
            var ghost = states[i].GetProperty("ghost");
            if (ghost.ValueKind == JsonValueKind.Object)
            {
                return ghost;
            }
        }

        return null;
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(25);
        }

        return false;
    }

    [Fact]
    public async Task Eating_a_power_pellet_frightens_the_ghost_and_it_recovers_when_the_window_lapses()
    {
        await using var match = await MatchDriver.StartAsync(_factory);

        var states = new List<JsonElement>();
        match.Runner.On<JsonElement>("StateUpdate", s => states.Add(s));

        match.Arrange(state =>
        {
            // Put Pac-Man on a power pellet, well away from the ghost so nothing else interferes.
            var power = state.Map.PowerPellets[0];
            state.Pacman!.X = power.X;
            state.Pacman.Y = power.Y;
            state.Ghost!.X = state.Map.GhostHouse.X;
            state.Ghost.Y = state.Map.GhostHouse.Y;
            state.Ghost.GhostSubState = GhostSubState.Normal;
        });

        Assert.True(
            await WaitForAsync(() => states.Count > 0 && ReadGhostSubState(states) == "Frightened"),
            "ghost never entered Frightened after the power pellet was eaten");

        // FR-006: speed drops to 70% for the duration.
        var frightenedSpeed = ReadGhost(states)!.Value.GetProperty("speedMultiplier").GetDouble();
        Assert.Equal(BalanceConstants.Movement.GhostSpeedFrightened, frightenedSpeed, precision: 4);

        // FR-005: and the window is 8 seconds, after which the ghost hunts again.
        Assert.True(
            await WaitForAsync(() => ReadGhostSubState(states) == "Normal", timeoutMs: 15_000),
            "ghost never returned to Normal after the 8s window elapsed");
    }

    [Fact]
    public async Task Catching_a_frightened_ghost_sends_it_home_and_locks_it_out()
    {
        await using var match = await MatchDriver.StartAsync(_factory);

        var states = new List<JsonElement>();
        var scoreEvents = new List<JsonElement>();
        match.Runner.On<JsonElement>("StateUpdate", s => states.Add(s));
        match.Runner.On<JsonElement>("ScoreEvent", e => scoreEvents.Add(e));

        match.Arrange(state =>
        {
            // Frightened, and overlapping Pac-Man so the next tick resolves the catch.
            state.Frightened = new FrightenedState { StartedAtMs = state.ElapsedMs };
            state.Ghost!.GhostSubState = GhostSubState.Frightened;
            state.Pacman!.X = 6;
            state.Pacman.Y = 5;
            state.Ghost.X = 6;
            state.Ghost.Y = 5;
        });

        Assert.True(
            await WaitForAsync(() =>
                scoreEvents.Any(e => e.GetProperty("eventType").GetString() == "GhostCaught")),
            "no GhostCaught score event was broadcast");

        // FR-009: first catch in the chain is worth 200.
        var caught = scoreEvents.First(e => e.GetProperty("eventType").GetString() == "GhostCaught");
        Assert.Equal(200, caught.GetProperty("points").GetInt32());
        Assert.Equal("Runner", caught.GetProperty("recipient").GetString());

        // Eyes head home, then serve the lockout, then rejoin the hunt.
        Assert.True(
            await WaitForAsync(() => ReadGhostSubState(states) is "EyesOnly" or "Respawning"),
            "ghost never entered the eyes-only return leg");

        Assert.True(
            await WaitForAsync(() => ReadGhostSubState(states) == "Normal", timeoutMs: 20_000),
            "ghost never re-entered play after the ghost-house lockout");
    }

    [Fact]
    public async Task A_frightened_ghost_touching_pacman_costs_no_life()
    {
        await using var match = await MatchDriver.StartAsync(_factory);

        var states = new List<JsonElement>();
        match.Runner.On<JsonElement>("StateUpdate", s => states.Add(s));

        match.Arrange(state =>
        {
            state.Frightened = new FrightenedState { StartedAtMs = state.ElapsedMs };
            state.Ghost!.GhostSubState = GhostSubState.Frightened;
            state.Pacman!.X = 6;
            state.Pacman.Y = 5;
            state.Ghost.X = 6;
            state.Ghost.Y = 5;
        });

        await WaitForAsync(() => states.Count > 5);

        var lives = states.Last(s2 => s2.GetProperty("pacman").ValueKind == JsonValueKind.Object)
            .GetProperty("pacman").GetProperty("livesRemaining").GetInt32();
        Assert.Equal(BalanceConstants.Match.PacmanLives, lives);
    }
}
