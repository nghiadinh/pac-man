using MatchServer.Engine;
using MatchServer.Generated;
using MatchServer.State;

namespace MatchServer.UnitTests;

/// <summary>
/// FR-021: when a normal-state elimination and a Power Pellet pickup land on the SAME tick, the
/// elimination applies first. The pellet is still consumed, but the Frightened window it creates
/// begins only once Pac-Man has respawned - eating a Power Pellet is a preemptive move, never a
/// last-instant save.
/// </summary>
public sealed class SimultaneousCollisionTests
{
    /// <summary>Pac-Man standing on a power pellet with a normal-state Ghost on the same tile.</summary>
    private static MatchState SameTickCollision()
    {
        var match = TestMatch.FromLayout(
            "#######",
            "#  o  #",
            "#######");

        match.Pacman().At(3, 1);
        match.Ghost().At(3, 1);
        match.Ghost().GhostSubState = GhostSubState.Normal;
        match.Ghost().RespawnReadyAtMs = null;
        return match;
    }

    /// <summary>Runs the tick in the pipeline's documented order: collisions, then pickups.</summary>
    private static (CollisionResult Collision, PelletResult Pellets) RunTick(MatchState match)
    {
        var collision = CollisionRules.Resolve(match);
        var pellets = PelletRules.Collect(match);

        if (pellets.PowerPelletEaten)
        {
            PelletRules.StartOrResetFrightened(match);
        }

        return (collision, pellets);
    }

    [Fact]
    public void Elimination_applies_first_so_pacman_still_loses_the_life()
    {
        var match = SameTickCollision();

        var (collision, _) = RunTick(match);

        Assert.True(collision.PacmanEliminated);
        Assert.Equal(BalanceConstants.Match.PacmanLives - 1, match.Pacman().LivesRemaining);
    }

    [Fact]
    public void The_power_pellet_is_still_consumed()
    {
        // The pellet is not refunded - it is spent, just too late to save the life.
        var match = SameTickCollision();
        var powerPellet = match.Map.PowerPellets.Single();

        RunTick(match);

        Assert.True(powerPellet.Collected);
    }

    [Fact]
    public void The_ghost_is_not_caught_on_the_tick_that_eliminated_pacman()
    {
        var match = SameTickCollision();

        var (collision, _) = RunTick(match);

        Assert.False(collision.GhostCaught);
        Assert.Equal(0, match.ScoreChain);
    }

    [Fact]
    public void The_ghost_is_sent_to_respawn_rather_than_being_frightened()
    {
        // If the pickup won the race the ghost would be Frightened here; because the elimination
        // wins, it is serving its FR-003 respawn delay instead.
        var match = SameTickCollision();

        RunTick(match);

        Assert.Equal(GhostSubState.Respawning, match.Ghost().GhostSubState);
    }

    [Fact]
    public void The_ghost_scores_for_the_elimination_and_pacman_scores_for_the_pellet()
    {
        var match = SameTickCollision();

        var (collision, pellets) = RunTick(match);

        Assert.Contains(collision.ScoreEvents, e => e.Type == ScoreEventType.PacmanEliminated);
        Assert.Contains(pellets.ScoreEvents, e => e.Type == ScoreEventType.PowerPelletCollected);
        Assert.Equal(BalanceConstants.Scoring.PacmanEliminatedPoints, match.Ghost().Score);
        Assert.Equal(BalanceConstants.Scoring.PowerPelletPoints, match.Pacman().Score);
    }

    [Fact]
    public void The_frightened_window_does_not_rescue_the_lost_life()
    {
        var match = SameTickCollision();
        var livesBefore = match.Pacman().LivesRemaining;

        RunTick(match);

        // A window may well be recorded, but the life is gone either way - that is the whole point
        // of the ordering.
        Assert.Equal(livesBefore - 1, match.Pacman().LivesRemaining);
    }

    [Fact]
    public void A_frightened_ghost_on_the_same_tile_is_caught_rather_than_eliminating_pacman()
    {
        // The mirror case: ordering only favours elimination when the ghost is in NORMAL state.
        var match = SameTickCollision();
        match.Frightened = new FrightenedState { StartedAtMs = 0 };
        match.Ghost().GhostSubState = GhostSubState.Frightened;

        var (collision, _) = RunTick(match);

        Assert.True(collision.GhostCaught);
        Assert.False(collision.PacmanEliminated);
        Assert.Equal(BalanceConstants.Match.PacmanLives, match.Pacman().LivesRemaining);
    }
}
