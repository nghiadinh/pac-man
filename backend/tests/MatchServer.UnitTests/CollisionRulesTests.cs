using MatchServer.Engine;
using MatchServer.Generated;
using MatchServer.State;

namespace MatchServer.UnitTests;

/// <summary>
/// FR-002 (three lives, ghost wins at zero), FR-003 (ghost respawn delay), and FR-004 (the
/// 0.8x0.8 tile collision box used by both players).
/// </summary>
public sealed class CollisionRulesTests
{
    private static MatchState Overlapping(double separation = 0)
    {
        var match = TestMatch.OpenCorridor();
        match.Pacman().At(3, 2);
        match.Ghost().At(3 + separation, 2);
        return match;
    }

    [Fact]
    public void Contact_in_normal_state_costs_pacman_one_life()
    {
        var match = Overlapping();

        var result = CollisionRules.Resolve(match);

        Assert.True(result.PacmanEliminated);
        Assert.Equal(BalanceConstants.Match.PacmanLives - 1, match.Pacman().LivesRemaining);
    }

    [Fact]
    public void Players_further_apart_than_the_collision_box_do_not_collide()
    {
        var match = Overlapping(separation: 1.0);

        var result = CollisionRules.Resolve(match);

        Assert.False(result.PacmanEliminated);
        Assert.Equal(BalanceConstants.Match.PacmanLives, match.Pacman().LivesRemaining);
    }

    [Fact]
    public void Collision_uses_the_zero_point_eight_tile_box()
    {
        // FR-004. Just inside the box collides; just outside does not.
        var box = BalanceConstants.Match.CollisionBoxTiles;

        Assert.True(CollisionRules.Resolve(Overlapping(box - 0.05)).PacmanEliminated);
        Assert.False(CollisionRules.Resolve(Overlapping(box + 0.05)).PacmanEliminated);
    }

    [Fact]
    public void Third_elimination_empties_the_life_counter()
    {
        var match = Overlapping();
        match.Pacman().LivesRemaining = 1;

        CollisionRules.Resolve(match);

        Assert.Equal(0, match.Pacman().LivesRemaining);
    }

    [Fact]
    public void Elimination_awards_the_ghost_five_hundred_points()
    {
        var match = Overlapping();

        var result = CollisionRules.Resolve(match);

        Assert.Contains(result.ScoreEvents, e =>
            e.Type == ScoreEventType.PacmanEliminated && e.Points == 500 && e.Recipient == Role.Hunter);
    }

    [Fact]
    public void Elimination_respawns_pacman_at_its_spawn_tile()
    {
        var match = Overlapping();
        match.Pacman().At(3, 2);

        CollisionRules.Resolve(match);

        Assert.Equal(match.Map.RunnerSpawn.X, match.Pacman().X, precision: 6);
        Assert.Equal(match.Map.RunnerSpawn.Y, match.Pacman().Y, precision: 6);
    }

    [Fact]
    public void Elimination_puts_the_ghost_into_a_five_second_respawn_delay()
    {
        // FR-003: unlimited respawns, but a 5-second delay after eliminating Pac-Man, which is
        // what stops the Ghost from instantly re-killing on the respawn tile.
        var match = Overlapping();
        match.ElapsedMs = 20_000;

        CollisionRules.Resolve(match);

        Assert.Equal(GhostSubState.Respawning, match.Ghost().GhostSubState);
        Assert.Equal(
            20_000 + BalanceConstants.Match.GhostRespawnDelayMs,
            match.Ghost().RespawnReadyAtMs);
    }

    [Fact]
    public void A_respawning_ghost_cannot_eliminate_pacman()
    {
        var match = Overlapping();
        match.Ghost().GhostSubState = GhostSubState.Respawning;
        match.Ghost().RespawnReadyAtMs = 10_000;

        var result = CollisionRules.Resolve(match);

        Assert.False(result.PacmanEliminated);
        Assert.Equal(BalanceConstants.Match.PacmanLives, match.Pacman().LivesRemaining);
    }

    [Fact]
    public void No_collision_is_resolved_once_the_match_has_ended()
    {
        var match = Overlapping();
        match.End(Role.Runner, MatchEndReason.PelletsCleared);

        var result = CollisionRules.Resolve(match);

        Assert.False(result.PacmanEliminated);
    }
}
