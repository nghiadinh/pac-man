using MatchServer.Generated;
using MatchServer.State;

namespace MatchServer.IntegrationTests;

/// <summary>
/// FR-016: the Ghost wins the instant Pac-Man's life counter reaches zero, and both clients are
/// told so through the real hub.
/// </summary>
public sealed class LivesDepletedTests : IDisposable
{
    private readonly MatchServerFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Ghost_wins_when_the_last_life_is_lost()
    {
        await using var match = await MatchDriver.StartAsync(_factory);

        match.Arrange(state =>
        {
            // One life left, and the two players overlapping so the next tick resolves contact.
            state.Pacman!.LivesRemaining = 1;
            state.Ghost!.GhostSubState = GhostSubState.Normal;
            state.Ghost.RespawnReadyAtMs = null;
            state.Pacman.X = 5;
            state.Pacman.Y = 5;
            state.Ghost.X = 5;
            state.Ghost.Y = 5;
        });

        var outcome = await match.WaitForEndAsync();

        outcome.ShouldBe(winner: "Hunter", reason: "LivesDepleted");
    }

    [Fact]
    public async Task Ghost_scores_five_hundred_for_the_elimination_that_ends_the_match()
    {
        await using var match = await MatchDriver.StartAsync(_factory);

        match.Arrange(state =>
        {
            state.Pacman!.LivesRemaining = 1;
            state.Pacman.X = 5;
            state.Pacman.Y = 5;
            state.Ghost!.X = 5;
            state.Ghost.Y = 5;
        });

        var outcome = await match.WaitForEndAsync();

        Assert.Equal(
            BalanceConstants.Scoring.PacmanEliminatedPoints,
            outcome.GetProperty("finalGhostScore").GetInt32());
    }

    [Fact]
    public async Task Both_clients_are_told_the_match_ended()
    {
        // SC-001 is only satisfied if BOTH players learn the outcome, not just one.
        await using var match = await MatchDriver.StartAsync(_factory);

        match.Arrange(state =>
        {
            state.Pacman!.LivesRemaining = 1;
            state.Pacman.X = 5;
            state.Pacman.Y = 5;
            state.Ghost!.X = 5;
            state.Ghost.Y = 5;
        });

        await match.WaitForEndAsync();

        var hunterNotified = await Task.WhenAny(
            match.HunterMatchEnded.Task,
            Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.True(hunterNotified == match.HunterMatchEnded.Task, "hunter never received MatchEnded");
        (await match.HunterMatchEnded.Task).ShouldBe("Hunter", "LivesDepleted");
    }

    [Fact]
    public async Task Losing_a_non_final_life_does_not_end_the_match()
    {
        await using var match = await MatchDriver.StartAsync(_factory);

        match.Arrange(state =>
        {
            state.Pacman!.LivesRemaining = 3;
            state.Pacman.X = 5;
            state.Pacman.Y = 5;
            state.Ghost!.X = 5;
            state.Ghost.Y = 5;
        });

        var ended = await Task.WhenAny(
            match.RunnerMatchEnded.Task,
            Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.True(ended != match.RunnerMatchEnded.Task, "match ended despite lives remaining");
    }
}
