using MatchServer.Engine;
using MatchServer.Generated;
using MatchServer.State;

namespace MatchServer.UnitTests;

/// <summary>
/// FR-015 (100% clear), FR-016 (lives depleted), FR-017 (timeout evaluation against the 70%
/// threshold), and FR-023 (an exact score tie goes to Pac-Man).
/// </summary>
public sealed class WinConditionRulesTests
{
    /// <summary>Board with a known pellet count so clear percentages are exact.</summary>
    private static MatchState TenPelletBoard()
    {
        var match = TestMatch.FromLayout(
            "############",
            "#..........#",
            "############");
        Assert.Equal(10, match.Map.TotalPelletCount);
        return match;
    }

    private static void Collect(MatchState match, int count)
    {
        foreach (var pellet in match.Map.Pellets.Take(count))
        {
            pellet.Collected = true;
        }
    }

    [Fact]
    public void Clearing_every_pellet_wins_for_pacman_immediately()
    {
        var match = TenPelletBoard();
        Collect(match, 10);

        WinConditionRules.Evaluate(match);

        Assert.Equal(MatchStatus.Ended, match.Status);
        Assert.Equal(Role.Runner, match.Outcome!.Winner);
        Assert.Equal(MatchEndReason.PelletsCleared, match.Outcome.Reason);
    }

    [Fact]
    public void Clearing_every_pellet_wins_regardless_of_score()
    {
        // FR-015 is an instant win, not a score comparison.
        var match = TenPelletBoard();
        Collect(match, 10);
        match.Ghost().Score = 99_999;

        WinConditionRules.Evaluate(match);

        Assert.Equal(Role.Runner, match.Outcome!.Winner);
    }

    [Fact]
    public void Losing_the_last_life_wins_for_the_ghost_immediately()
    {
        var match = TenPelletBoard();
        match.Pacman().LivesRemaining = 0;

        WinConditionRules.Evaluate(match);

        Assert.Equal(MatchStatus.Ended, match.Status);
        Assert.Equal(Role.Hunter, match.Outcome!.Winner);
        Assert.Equal(MatchEndReason.LivesDepleted, match.Outcome.Reason);
    }

    [Fact]
    public void Match_does_not_end_while_the_clock_runs_and_lives_remain()
    {
        var match = TenPelletBoard();
        Collect(match, 5);
        match.ElapsedMs = 60_000;

        WinConditionRules.Evaluate(match);

        Assert.Equal(MatchStatus.Active, match.Status);
        Assert.Null(match.Outcome);
    }

    [Fact]
    public void Timeout_below_seventy_percent_wins_for_the_ghost()
    {
        var match = TenPelletBoard();
        Collect(match, 6); // 60%
        match.ElapsedMs = BalanceConstants.Match.MatchDurationMs;
        match.Pacman().Score = 10_000; // score is irrelevant below the threshold

        WinConditionRules.Evaluate(match);

        Assert.Equal(Role.Hunter, match.Outcome!.Winner);
        Assert.Equal(MatchEndReason.TimeoutClearThresholdMissed, match.Outcome.Reason);
    }

    [Fact]
    public void Exactly_seventy_percent_meets_the_threshold_and_proceeds_to_scores()
    {
        // Boundary case from spec Edge Cases: 70% satisfies ">= 70%", so it is NOT an automatic
        // ghost win - the score comparison decides.
        var match = TenPelletBoard();
        Collect(match, 7); // exactly 70%
        match.ElapsedMs = BalanceConstants.Match.MatchDurationMs;
        match.Pacman().Score = 100;
        match.Ghost().Score = 50;

        WinConditionRules.Evaluate(match);

        Assert.Equal(Role.Runner, match.Outcome!.Winner);
        Assert.Equal(MatchEndReason.TimeoutClearThresholdMet, match.Outcome.Reason);
    }

    [Fact]
    public void Above_threshold_but_lower_score_wins_for_the_ghost()
    {
        var match = TenPelletBoard();
        Collect(match, 9);
        match.ElapsedMs = BalanceConstants.Match.MatchDurationMs;
        match.Pacman().Score = 100;
        match.Ghost().Score = 500;

        WinConditionRules.Evaluate(match);

        Assert.Equal(Role.Hunter, match.Outcome!.Winner);
    }

    [Fact]
    public void An_exact_score_tie_at_or_above_threshold_goes_to_pacman()
    {
        // FR-023, clarified 2026-08-14.
        var match = TenPelletBoard();
        Collect(match, 8);
        match.ElapsedMs = BalanceConstants.Match.MatchDurationMs;
        match.Pacman().Score = 500;
        match.Ghost().Score = 500;

        WinConditionRules.Evaluate(match);

        Assert.Equal(Role.Runner, match.Outcome!.Winner);
        Assert.Equal(MatchEndReason.TimeoutClearThresholdMet, match.Outcome.Reason);
    }

    [Fact]
    public void Every_terminal_path_produces_an_outcome_so_no_match_ends_undecided()
    {
        // SC-001. Exercised across all four end conditions.
        foreach (var setup in new Action<MatchState>[]
        {
            m => Collect(m, 10),
            m => m.Pacman().LivesRemaining = 0,
            m => { Collect(m, 9); m.ElapsedMs = BalanceConstants.Match.MatchDurationMs; },
            m => { Collect(m, 2); m.ElapsedMs = BalanceConstants.Match.MatchDurationMs; },
        })
        {
            var match = TenPelletBoard();
            setup(match);

            WinConditionRules.Evaluate(match);

            Assert.Equal(MatchStatus.Ended, match.Status);
            Assert.NotNull(match.Outcome);
        }
    }

    [Fact]
    public void Pacman_receives_the_time_bonus_only_on_a_full_clear()
    {
        // FR-018: the +5/second bonus is conditional on 100% of pellets being cleared.
        var match = TenPelletBoard();
        Collect(match, 10);
        match.ElapsedMs = BalanceConstants.Match.MatchDurationMs - 20_000; // 20s left

        WinConditionRules.Evaluate(match);

        Assert.Equal(100, match.Outcome!.FinalPacmanScore);
    }

    [Fact]
    public void No_time_bonus_when_the_clock_runs_out()
    {
        var match = TenPelletBoard();
        Collect(match, 9);
        match.ElapsedMs = BalanceConstants.Match.MatchDurationMs;

        WinConditionRules.Evaluate(match);

        Assert.Equal(0, match.Outcome!.FinalPacmanScore);
    }
}
