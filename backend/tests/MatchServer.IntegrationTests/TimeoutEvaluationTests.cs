using MatchServer.Generated;
using MatchServer.State;

namespace MatchServer.IntegrationTests;

/// <summary>
/// FR-017 timeout evaluation and the FR-023 tie-break, exercised through the real hub across all
/// three branches: below the threshold, at or above it with a score lead, and an exact tie.
/// </summary>
public sealed class TimeoutEvaluationTests : IDisposable
{
    private readonly MatchServerFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    /// <summary>
    /// Marks pellets collected until the given fraction of the board is cleared, then parks the
    /// clock one tick short of expiry so the very next tick runs the timeout evaluation.
    /// </summary>
    private static void ArrangeTimeout(
        MatchState state, double clearedFraction, int pacmanScore, int ghostScore)
    {
        var target = (int)Math.Round(state.Map.TotalPelletCount * clearedFraction);

        var collected = 0;
        foreach (var pellet in state.Map.Pellets)
        {
            if (collected >= target) break;
            pellet.Collected = true;
            collected++;
        }

        state.Pacman!.Score = pacmanScore;
        state.Ghost!.Score = ghostScore;

        // Keep the players apart so a stray collision cannot decide the match first.
        state.Pacman.X = 1;
        state.Pacman.Y = 1;
        state.Ghost.X = state.Map.Width - 2;
        state.Ghost.Y = state.Map.Height - 2;

        state.ElapsedMs = BalanceConstants.Match.MatchDurationMs - 1;
    }

    [Fact]
    public async Task Below_seventy_percent_the_ghost_wins_regardless_of_score()
    {
        await using var match = await MatchDriver.StartAsync(_factory);

        match.Arrange(state => ArrangeTimeout(state, 0.50, pacmanScore: 10_000, ghostScore: 0));

        var outcome = await match.WaitForEndAsync();

        outcome.ShouldBe(winner: "Hunter", reason: "TimeoutClearThresholdMissed");
    }

    [Fact]
    public async Task At_or_above_the_threshold_a_score_lead_wins_for_pacman()
    {
        await using var match = await MatchDriver.StartAsync(_factory);

        match.Arrange(state => ArrangeTimeout(state, 0.85, pacmanScore: 900, ghostScore: 500));

        var outcome = await match.WaitForEndAsync();

        outcome.ShouldBe(winner: "Runner", reason: "TimeoutClearThresholdMet");
    }

    [Fact]
    public async Task At_or_above_the_threshold_a_score_deficit_wins_for_the_ghost()
    {
        await using var match = await MatchDriver.StartAsync(_factory);

        match.Arrange(state => ArrangeTimeout(state, 0.85, pacmanScore: 500, ghostScore: 1_500));

        var outcome = await match.WaitForEndAsync();

        outcome.ShouldBe(winner: "Hunter", reason: "TimeoutClearThresholdMissed");
    }

    [Fact]
    public async Task An_exact_tie_at_or_above_the_threshold_goes_to_pacman()
    {
        // FR-023, clarified 2026-08-14: the tie-break favours Pac-Man because he has already
        // satisfied the primary clear-rate condition.
        await using var match = await MatchDriver.StartAsync(_factory);

        match.Arrange(state => ArrangeTimeout(state, 0.85, pacmanScore: 1_000, ghostScore: 1_000));

        var outcome = await match.WaitForEndAsync();

        outcome.ShouldBe(winner: "Runner", reason: "TimeoutClearThresholdMet");
    }

    [Fact]
    public async Task Exactly_seventy_percent_counts_as_meeting_the_threshold()
    {
        // Spec Edge Case: 70% satisfies ">= 70%", so it proceeds to the score comparison rather
        // than being an automatic Ghost win.
        await using var match = await MatchDriver.StartAsync(_factory);

        match.Arrange(state => ArrangeTimeout(state, 0.70, pacmanScore: 100, ghostScore: 0));

        var outcome = await match.WaitForEndAsync();

        outcome.ShouldBe(winner: "Runner", reason: "TimeoutClearThresholdMet");
    }
}
