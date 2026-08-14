namespace MatchServer.IntegrationTests;

/// <summary>
/// FR-015: clearing 100% of pellets before the clock expires wins for Pac-Man immediately,
/// regardless of score or remaining time.
/// </summary>
public sealed class PelletsClearedTests : IDisposable
{
    private readonly MatchServerFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Pacman_wins_the_moment_the_board_is_clear()
    {
        await using var match = await MatchDriver.StartAsync(_factory);

        match.Arrange(state =>
        {
            foreach (var pellet in state.Map.Pellets)
            {
                pellet.Collected = true;
            }

            foreach (var power in state.Map.PowerPellets)
            {
                power.Collected = true;
            }
        });

        var outcome = await match.WaitForEndAsync();

        outcome.ShouldBe(winner: "Runner", reason: "PelletsCleared");
    }

    [Fact]
    public async Task A_full_clear_wins_even_when_the_ghost_leads_on_score()
    {
        // FR-015 is an instant win, not a score comparison - a Ghost that has eliminated Pac-Man
        // twice still loses to a completed board.
        await using var match = await MatchDriver.StartAsync(_factory);

        match.Arrange(state =>
        {
            state.Ghost!.Score = 1_000;
            foreach (var pellet in state.Map.Pellets) pellet.Collected = true;
            foreach (var power in state.Map.PowerPellets) power.Collected = true;
        });

        var outcome = await match.WaitForEndAsync();

        outcome.ShouldBe(winner: "Runner", reason: "PelletsCleared");
    }

    [Fact]
    public async Task Clearing_the_board_awards_the_time_bonus()
    {
        // FR-018: +5 per second remaining, and only on a 100% clear.
        await using var match = await MatchDriver.StartAsync(_factory);

        match.Arrange(state =>
        {
            state.Pacman!.Score = 0;
            state.ElapsedMs = 150_000; // 30s left -> 150 points
            foreach (var pellet in state.Map.Pellets) pellet.Collected = true;
            foreach (var power in state.Map.PowerPellets) power.Collected = true;
        });

        var outcome = await match.WaitForEndAsync();

        var finalScore = outcome.GetProperty("finalPacmanScore").GetInt32();
        Assert.InRange(finalScore, 100, 160); // tick granularity around the 30s mark
    }

    [Fact]
    public async Task One_pellet_short_does_not_end_the_match()
    {
        await using var match = await MatchDriver.StartAsync(_factory);

        match.Arrange(state =>
        {
            foreach (var pellet in state.Map.Pellets.Skip(1)) pellet.Collected = true;
            foreach (var power in state.Map.PowerPellets) power.Collected = true;
        });

        var ended = await Task.WhenAny(
            match.RunnerMatchEnded.Task,
            Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.True(ended != match.RunnerMatchEnded.Task, "match ended before the board was clear");
    }
}
