using MatchServer.Engine;
using MatchServer.Generated;
using MatchServer.State;

namespace MatchServer.UnitTests;

/// <summary>FR-006 (70% frightened speed) and FR-007 (3.0s directional input inversion).</summary>
public sealed class FrightenedMovementTests
{
    private static MatchState Frightened(long elapsedMs = 0)
    {
        var match = TestMatch.OpenCorridor();
        match.ElapsedMs = elapsedMs;
        match.Frightened = new FrightenedState { StartedAtMs = 0 };
        match.Ghost().GhostSubState = GhostSubState.Frightened;
        return match;
    }

    [Fact]
    public void Frightened_ghost_moves_at_seventy_percent()
    {
        var match = Frightened();

        Assert.Equal(
            BalanceConstants.Movement.GhostSpeedFrightened,
            MovementRules.EffectiveSpeed(match.Ghost(), match));
    }

    [Fact]
    public void Frightened_ghost_is_slower_than_its_own_normal_speed()
    {
        var match = Frightened();
        var frightenedSpeed = MovementRules.EffectiveSpeed(match.Ghost(), match);

        match.Ghost().GhostSubState = GhostSubState.Normal;
        var normalSpeed = MovementRules.EffectiveSpeed(match.Ghost(), match);

        Assert.True(frightenedSpeed < normalSpeed);
    }

    [Fact]
    public void Pacman_outpaces_a_frightened_ghost_by_a_wide_margin()
    {
        // This is what makes the role reversal meaningful: 1.00 vs 0.70 is a real chase advantage,
        // unlike the razor-thin 1.00 vs 0.95 of normal play.
        var match = Frightened();

        var runner = MovementRules.EffectiveSpeed(match.Pacman(), match);
        var hunter = MovementRules.EffectiveSpeed(match.Ghost(), match);

        Assert.Equal(0.70, hunter / runner, precision: 6);
    }

    [Fact]
    public void Hunter_input_is_inverted_during_the_first_three_seconds()
    {
        var match = Frightened(elapsedMs: 500);
        var ghost = match.Ghost().At(3, 2);
        ghost.Facing = Direction.None;
        ghost.DesiredDirection = Direction.Right;

        MovementRules.Advance(match, deltaMs: 16);

        // FR-007: the player pressed Right, so the ghost must actually go Left.
        Assert.Equal(Direction.Left, ghost.Facing);
    }

    [Theory]
    [InlineData(Direction.Up, Direction.Down)]
    [InlineData(Direction.Down, Direction.Up)]
    [InlineData(Direction.Left, Direction.Right)]
    [InlineData(Direction.Right, Direction.Left)]
    public void Every_axis_is_inverted(Direction pressed, Direction expected)
    {
        var match = Frightened(elapsedMs: 100);
        var ghost = match.Ghost().At(3, 2);
        ghost.Facing = Direction.None;
        ghost.DesiredDirection = pressed;

        MovementRules.Advance(match, deltaMs: 16);

        Assert.Equal(expected, ghost.Facing);
    }

    [Fact]
    public void Input_returns_to_normal_after_the_inversion_window_closes()
    {
        var match = Frightened(elapsedMs: BalanceConstants.Frightened.FrightenedInversionMs + 100);
        var ghost = match.Ghost().At(3, 2);
        ghost.Facing = Direction.None;
        ghost.DesiredDirection = Direction.Right;

        MovementRules.Advance(match, deltaMs: 16);

        Assert.Equal(Direction.Right, ghost.Facing);
    }

    [Fact]
    public void Runner_input_is_never_inverted()
    {
        // The debuff belongs to the Hunter alone - Pac-Man keeps clean controls throughout.
        var match = Frightened(elapsedMs: 500);
        var pacman = match.Pacman().At(3, 2);
        pacman.Facing = Direction.None;
        pacman.DesiredDirection = Direction.Right;

        MovementRules.Advance(match, deltaMs: 16);

        Assert.Equal(Direction.Right, pacman.Facing);
    }

    [Fact]
    public void Eyes_only_ghost_travels_at_one_hundred_fifty_percent()
    {
        var match = TestMatch.OpenCorridor();
        match.Ghost().GhostSubState = GhostSubState.EyesOnly;

        Assert.Equal(
            BalanceConstants.Movement.EyesSpeed,
            MovementRules.EffectiveSpeed(match.Ghost(), match));
    }

    [Fact]
    public void Eyes_only_ghost_is_not_slowed_by_the_frightened_state_it_came_from()
    {
        var match = Frightened();
        match.Ghost().GhostSubState = GhostSubState.EyesOnly;

        Assert.Equal(
            BalanceConstants.Movement.EyesSpeed,
            MovementRules.EffectiveSpeed(match.Ghost(), match));
    }

    [Fact]
    public void A_respawning_ghost_does_not_move()
    {
        var match = TestMatch.OpenCorridor();
        var ghost = match.Ghost().At(3, 2).Heading(Direction.Right);
        ghost.GhostSubState = GhostSubState.Respawning;

        MovementRules.Advance(match, deltaMs: 500);

        Assert.Equal(3, ghost.X, precision: 6);
    }
}
