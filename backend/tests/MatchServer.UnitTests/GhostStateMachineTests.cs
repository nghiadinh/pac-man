using MatchServer.Engine;
using MatchServer.Generated;
using MatchServer.State;

namespace MatchServer.UnitTests;

/// <summary>
/// The GhostSubState machine (data-model.md) and the FR-009 return-and-lockout sequence.
/// Includes the spec Edge Case that a Ghost already in EyesOnly cannot be caught again.
/// </summary>
public sealed class GhostStateMachineTests
{
    private static MatchState Board()
    {
        var match = TestMatch.FromLayout(
            "#########",
            "#   G   #",
            "#       #",
            "#   P   #",
            "#########");
        return match;
    }

    [Fact]
    public void A_ghost_already_in_eyes_only_cannot_be_caught_again()
    {
        // Spec Edge Case: it must complete its return-and-lockout before becoming a valid target,
        // otherwise Pac-Man could farm the chain by walking alongside the eyes on their way home.
        var match = Board();
        match.Ghost().GhostSubState = GhostSubState.EyesOnly;
        match.Pacman().At(4, 1);
        match.Ghost().At(4, 1);

        var result = CollisionRules.Resolve(match);

        Assert.False(result.GhostCaught);
        Assert.Empty(result.ScoreEvents);
        Assert.Equal(0, match.ScoreChain);
    }

    [Fact]
    public void A_respawning_ghost_cannot_be_caught_either()
    {
        var match = Board();
        match.Ghost().GhostSubState = GhostSubState.Respawning;
        match.Pacman().At(4, 1);
        match.Ghost().At(4, 1);

        var result = CollisionRules.Resolve(match);

        Assert.False(result.GhostCaught);
        Assert.False(result.PacmanEliminated);
    }

    [Fact]
    public void Eyes_reaching_the_ghost_house_start_the_five_second_lockout()
    {
        var match = Board();
        match.ElapsedMs = 10_000;
        match.Ghost().GhostSubState = GhostSubState.EyesOnly;
        match.Ghost().At(match.Map.GhostHouse.X, match.Map.GhostHouse.Y);

        GhostStateMachine.Advance(match);

        Assert.Equal(GhostSubState.Respawning, match.Ghost().GhostSubState);
        Assert.Equal(
            10_000 + BalanceConstants.Frightened.GhostHouseLockoutMs,
            match.Ghost().RespawnReadyAtMs);
    }

    [Fact]
    public void Eyes_still_travelling_do_not_start_the_lockout()
    {
        var match = Board();
        match.Ghost().GhostSubState = GhostSubState.EyesOnly;
        match.Ghost().At(1, 3); // far from the ghost house

        GhostStateMachine.Advance(match);

        Assert.Equal(GhostSubState.EyesOnly, match.Ghost().GhostSubState);
        Assert.Null(match.Ghost().RespawnReadyAtMs);
    }

    [Fact]
    public void The_ghost_returns_to_normal_once_the_lockout_expires()
    {
        var match = Board();
        match.Ghost().GhostSubState = GhostSubState.Respawning;
        match.Ghost().RespawnReadyAtMs = 5_000;
        match.ElapsedMs = 5_000;

        GhostStateMachine.Advance(match);

        Assert.Equal(GhostSubState.Normal, match.Ghost().GhostSubState);
        Assert.Null(match.Ghost().RespawnReadyAtMs);
    }

    [Fact]
    public void The_ghost_stays_locked_out_until_the_timer_elapses()
    {
        var match = Board();
        match.Ghost().GhostSubState = GhostSubState.Respawning;
        match.Ghost().RespawnReadyAtMs = 5_000;
        match.ElapsedMs = 4_999;

        GhostStateMachine.Advance(match);

        Assert.Equal(GhostSubState.Respawning, match.Ghost().GhostSubState);
    }

    [Fact]
    public void A_frightened_ghost_returns_to_normal_when_the_window_lapses_uncaught()
    {
        var match = Board();
        match.Frightened = new FrightenedState { StartedAtMs = 0 };
        match.Ghost().GhostSubState = GhostSubState.Frightened;
        match.ElapsedMs = BalanceConstants.Frightened.FrightenedDurationMs;

        GhostStateMachine.Advance(match);

        Assert.Equal(GhostSubState.Normal, match.Ghost().GhostSubState);
    }

    [Fact]
    public void An_expired_window_clears_the_catch_chain()
    {
        var match = Board();
        match.Frightened = new FrightenedState { StartedAtMs = 0 };
        match.Ghost().GhostSubState = GhostSubState.Frightened;
        match.ScoreChain = 2;
        match.ElapsedMs = BalanceConstants.Frightened.FrightenedDurationMs;

        GhostStateMachine.Advance(match);

        Assert.Equal(0, match.ScoreChain);
    }

    [Fact]
    public void A_frightened_ghost_stays_frightened_while_the_window_is_open()
    {
        var match = Board();
        match.Frightened = new FrightenedState { StartedAtMs = 0 };
        match.Ghost().GhostSubState = GhostSubState.Frightened;
        match.ElapsedMs = 4_000;

        GhostStateMachine.Advance(match);

        Assert.Equal(GhostSubState.Frightened, match.Ghost().GhostSubState);
    }

    [Fact]
    public void Eyes_head_toward_the_ghost_house_rather_than_wandering()
    {
        var match = Board();
        match.Ghost().GhostSubState = GhostSubState.EyesOnly;
        match.Ghost().At(1, 3);

        var before = Distance(match.Ghost(), match.Map.GhostHouse);
        GhostStateMachine.Advance(match);
        MovementRules.Advance(match, deltaMs: 200);
        var after = Distance(match.Ghost(), match.Map.GhostHouse);

        Assert.True(after < before, $"eyes moved away from home: {before} -> {after}");
    }

    private static double Distance(PlayerState player, (int X, int Y) target) =>
        Math.Abs(player.X - target.X) + Math.Abs(player.Y - target.Y);
}
