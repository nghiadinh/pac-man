using System.Text.Json;
using MatchServer.State;
using Microsoft.AspNetCore.SignalR.Client;

namespace MatchServer.IntegrationTests;

/// <summary>
/// FR-010 / FR-011 enforced on the WIRE.
/// </summary>
/// <remarks>
/// This is the test that matters most for Constitution Principle III. A client that receives data
/// it is not supposed to use is an extractable fairness bug no matter what the UI does with it -
/// so the assertion is that the Hunter's connection never RECEIVES the Runner's position, not that
/// it declines to render it.
/// </remarks>
public sealed class FogOfWarFilteringTests : IDisposable
{
    private readonly MatchServerFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMs = 15_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(25);
        }

        return false;
    }

    /// <summary>A withheld position arrives as NaN, which JSON encodes as null.</summary>
    private static bool PositionWithheld(JsonElement state)
    {
        var pacman = state.GetProperty("pacman");
        if (pacman.ValueKind != JsonValueKind.Object) return true;

        var x = pacman.GetProperty("x");
        return x.ValueKind == JsonValueKind.Null ||
               (x.ValueKind == JsonValueKind.Number && double.IsNaN(x.GetDouble()));
    }

    [Fact]
    public async Task Hunter_never_receives_the_runner_position_while_it_is_out_of_sight()
    {
        await using var match = await MatchDriver.StartAsync(_factory);

        var hunterStates = new List<JsonElement>();
        match.Hunter.On<JsonElement>("StateUpdate", s => hunterStates.Add(s));

        match.Arrange(state =>
        {
            // Opposite corners of the maze, with walls between - far outside the 6-tile radius and
            // sharing neither a row nor a column.
            state.Pacman!.X = 1;
            state.Pacman.Y = 1;
            state.Ghost!.X = state.Map.Width - 2;
            state.Ghost.Y = state.Map.Height - 2;
            state.Ghost.GhostSubState = GhostSubState.Normal;
        });

        Assert.True(await WaitForAsync(() => hunterStates.Count > 10), "no state arrived");

        // Every snapshot from the point the players were separated must withhold the position.
        var recent = hunterStates.TakeLast(5).ToList();
        Assert.All(recent, s => Assert.True(
            PositionWithheld(s),
            "hunter received the runner's true position while it should have been fogged"));
    }

    [Fact]
    public async Task Hunter_receives_the_runner_position_once_it_is_within_the_radius()
    {
        await using var match = await MatchDriver.StartAsync(_factory);

        var hunterStates = new List<JsonElement>();
        match.Hunter.On<JsonElement>("StateUpdate", s => hunterStates.Add(s));

        match.Arrange(state =>
        {
            state.Pacman!.X = 6;
            state.Pacman.Y = 5;
            state.Ghost!.X = 7;
            state.Ghost.Y = 5;
            state.Ghost.GhostSubState = GhostSubState.Normal;
        });

        Assert.True(
            await WaitForAsync(() => hunterStates.Count > 5 && !PositionWithheld(hunterStates[^1])),
            "hunter never received the runner's position despite standing next to it");
    }

    [Fact]
    public async Task Runner_is_never_fogged_and_always_sees_the_hunter()
    {
        // FR-010 is absolute - there is no condition under which Pac-Man's view is restricted.
        await using var match = await MatchDriver.StartAsync(_factory);

        var runnerStates = new List<JsonElement>();
        match.Runner.On<JsonElement>("StateUpdate", s => runnerStates.Add(s));

        match.Arrange(state =>
        {
            state.Pacman!.X = 1;
            state.Pacman.Y = 1;
            state.Ghost!.X = state.Map.Width - 2;
            state.Ghost.Y = state.Map.Height - 2;
        });

        Assert.True(await WaitForAsync(() => runnerStates.Count > 10), "no state arrived");

        var recent = runnerStates.TakeLast(5).ToList();
        Assert.All(recent, s =>
        {
            var ghost = s.GetProperty("ghost");
            Assert.Equal(JsonValueKind.Object, ghost.ValueKind);

            var x = ghost.GetProperty("x");
            Assert.True(
                x.ValueKind == JsonValueKind.Number && !double.IsNaN(x.GetDouble()),
                "runner was denied the ghost's position, violating FR-010");
        });
    }

    [Fact]
    public async Task Hunter_receives_sonar_pulses_carrying_only_a_quadrant()
    {
        await using var match = await MatchDriver.StartAsync(_factory);

        var pulses = new List<string>();
        match.Hunter.On<string>("SonarPulse", q => pulses.Add(q));

        match.Arrange(state =>
        {
            state.Pacman!.X = 1;
            state.Pacman.Y = 1;
            state.Ghost!.X = state.Map.Width - 2;
            state.Ghost.Y = state.Map.Height - 2;
        });

        Assert.True(await WaitForAsync(() => pulses.Count > 0), "no sonar pulse arrived");

        // Pac-Man is top-left, so the quadrant must be NW - and must be a bare quadrant token
        // with no coordinates attached.
        Assert.Equal("NW", pulses[0]);
        Assert.All(pulses, p => Assert.Contains(p, new[] { "NE", "NW", "SE", "SW" }));
    }

    [Fact]
    public async Task Runner_receives_no_sonar_pulses()
    {
        // Sonar exists to partially compensate the Hunter's disadvantage; sending it to the
        // Runner would be meaningless noise and leak the Hunter's own blindness state.
        await using var match = await MatchDriver.StartAsync(_factory);

        var runnerPulses = new List<string>();
        match.Runner.On<string>("SonarPulse", q => runnerPulses.Add(q));

        match.Arrange(state =>
        {
            state.Pacman!.X = 1;
            state.Pacman.Y = 1;
            state.Ghost!.X = state.Map.Width - 2;
            state.Ghost.Y = state.Map.Height - 2;
        });

        await Task.Delay(6_000); // more than one full sonar interval

        Assert.Empty(runnerPulses);
    }
}
