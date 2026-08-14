using MatchServer.Engine;
using MatchServer.Generated;
using MatchServer.State;

namespace MatchServer.UnitTests;

/// <summary>
/// Spec Edge Case: the match timer reaching 0:00 on the SAME tick Pac-Man collects the final
/// pellet. Constitution Principle II requires exactly one documented outcome for this, and the
/// spec resolves it in Pac-Man's favour - the instant-clear victory (FR-015) takes precedence
/// over timeout evaluation (FR-017).
/// </summary>
public sealed class WinConditionSimultaneityTests
{
    private static MatchState BoardWithOnePelletLeft()
    {
        var match = TestMatch.FromLayout(
            "############",
            "#..........#",
            "############");

        foreach (var pellet in match.Map.Pellets.Take(9))
        {
            pellet.Collected = true;
        }

        return match;
    }

    [Fact]
    public void Full_clear_on_the_expiry_tick_wins_for_pacman_not_the_timeout_path()
    {
        var match = BoardWithOnePelletLeft();

        // Both conditions become true on the same tick.
        match.Map.Pellets[9].Collected = true;
        match.ElapsedMs = BalanceConstants.Match.MatchDurationMs;

        WinConditionRules.Evaluate(match);

        Assert.Equal(Role.Runner, match.Outcome!.Winner);
        Assert.Equal(MatchEndReason.PelletsCleared, match.Outcome.Reason);
    }

    [Fact]
    public void Full_clear_on_the_expiry_tick_wins_even_when_the_ghost_leads_on_score()
    {
        // Without FR-015 taking precedence this would fall through to the score comparison and
        // hand the Ghost a win, despite Pac-Man having cleared the entire board.
        var match = BoardWithOnePelletLeft();
        match.Map.Pellets[9].Collected = true;
        match.ElapsedMs = BalanceConstants.Match.MatchDurationMs;
        match.Ghost().Score = 5_000;
        match.Pacman().Score = 0;

        WinConditionRules.Evaluate(match);

        Assert.Equal(Role.Runner, match.Outcome!.Winner);
        Assert.Equal(MatchEndReason.PelletsCleared, match.Outcome.Reason);
    }

    [Fact]
    public void Lives_reaching_zero_on_the_expiry_tick_wins_for_the_ghost()
    {
        // The mirror case: an elimination that empties the life counter on the same tick the
        // clock expires is still a FR-016 instant win, not a timeout evaluation.
        var match = BoardWithOnePelletLeft();
        match.Pacman().LivesRemaining = 0;
        match.ElapsedMs = BalanceConstants.Match.MatchDurationMs;

        WinConditionRules.Evaluate(match);

        Assert.Equal(Role.Hunter, match.Outcome!.Winner);
        Assert.Equal(MatchEndReason.LivesDepleted, match.Outcome.Reason);
    }

    [Fact]
    public void A_decided_match_is_never_overwritten_by_a_later_evaluation()
    {
        // MatchState.End is first-write-wins, so a forfeit racing a natural end cannot rewrite
        // an already-announced result.
        var match = BoardWithOnePelletLeft();
        match.End(Role.Hunter, MatchEndReason.Forfeit);

        match.Map.Pellets[9].Collected = true;
        WinConditionRules.Evaluate(match);

        Assert.Equal(Role.Hunter, match.Outcome!.Winner);
        Assert.Equal(MatchEndReason.Forfeit, match.Outcome.Reason);
    }
}
