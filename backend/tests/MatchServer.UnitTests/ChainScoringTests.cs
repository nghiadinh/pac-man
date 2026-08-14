using MatchServer.Engine;
using MatchServer.State;

namespace MatchServer.UnitTests;

/// <summary>FR-009: 200/400/800/1600 for consecutive catches, resetting between chains.</summary>
public sealed class ChainScoringTests
{
    private static MatchState FrightenedMatch()
    {
        var match = TestMatch.OpenCorridor();
        match.Frightened = new FrightenedState { StartedAtMs = 0 };
        match.Ghost().GhostSubState = GhostSubState.Frightened;
        match.Pacman().At(3, 2);
        match.Ghost().At(3, 2);
        return match;
    }

    [Fact]
    public void Four_catches_in_one_window_escalate_through_the_full_chain()
    {
        var match = FrightenedMatch();
        var awarded = new List<int>();

        for (var i = 0; i < 4; i++)
        {
            // Re-frighten and re-overlap to simulate catching the same ghost again in one window.
            match.Ghost().GhostSubState = GhostSubState.Frightened;
            match.Ghost().At(3, 2);

            var result = CollisionRules.Resolve(match);
            awarded.Add(result.ScoreEvents.Single().Points);
        }

        Assert.Equal([200, 400, 800, 1600], awarded);
        Assert.Equal(3000, match.Pacman().Score);
    }

    [Fact]
    public void A_new_frightened_window_restarts_the_chain_at_two_hundred()
    {
        var match = FrightenedMatch();

        CollisionRules.Resolve(match); // 200
        match.Ghost().GhostSubState = GhostSubState.Frightened;
        match.Ghost().At(3, 2);
        CollisionRules.Resolve(match); // 400

        Assert.Equal(2, match.ScoreChain);

        // A fresh Power Pellet resets the chain (FR-009).
        PelletRules.StartOrResetFrightened(match);
        Assert.Equal(0, match.ScoreChain);

        match.Ghost().GhostSubState = GhostSubState.Frightened;
        match.Ghost().At(3, 2);
        var result = CollisionRules.Resolve(match);

        Assert.Equal(200, result.ScoreEvents.Single().Points);
    }

    [Fact]
    public void Points_are_clamped_at_the_top_of_the_chain()
    {
        Assert.Equal(1600, ScoringRules.ChainPoints(4));
        Assert.Equal(1600, ScoringRules.ChainPoints(99));
    }

    [Fact]
    public void Chain_points_never_index_below_the_start()
    {
        Assert.Equal(200, ScoringRules.ChainPoints(0));
        Assert.Equal(200, ScoringRules.ChainPoints(-1));
    }

    [Fact]
    public void Catching_a_frightened_ghost_sends_it_to_eyes_only()
    {
        var match = FrightenedMatch();

        var result = CollisionRules.Resolve(match);

        Assert.True(result.GhostCaught);
        Assert.False(result.PacmanEliminated);
        Assert.Equal(GhostSubState.EyesOnly, match.Ghost().GhostSubState);
    }

    [Fact]
    public void Catching_a_ghost_costs_pacman_no_life()
    {
        var match = FrightenedMatch();
        var lives = match.Pacman().LivesRemaining;

        CollisionRules.Resolve(match);

        Assert.Equal(lives, match.Pacman().LivesRemaining);
    }
}
