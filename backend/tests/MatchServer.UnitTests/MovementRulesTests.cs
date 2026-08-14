using MatchServer.Engine;
using MatchServer.Generated;
using MatchServer.State;

namespace MatchServer.UnitTests;

/// <summary>
/// FR-001: Pac-Man moves at 100% base grid speed with no cornering loss; the Ghost moves at 95%
/// and takes a multiplicative 0.95 cornering penalty until it reaches the next tile center.
/// </summary>
public sealed class MovementRulesTests
{
    [Fact]
    public void Runner_always_moves_at_full_base_speed()
    {
        var match = TestMatch.OpenCorridor();
        var pacman = match.Pacman().At(2, 2).Heading(Direction.Right);

        Assert.Equal(
            BalanceConstants.Movement.PacmanBaseSpeed,
            MovementRules.EffectiveSpeed(pacman, match));
    }

    [Fact]
    public void Hunter_moves_at_ninety_five_percent_in_normal_state()
    {
        var match = TestMatch.OpenCorridor();
        var ghost = match.Ghost().At(2, 2).Heading(Direction.Left);

        Assert.Equal(
            BalanceConstants.Movement.GhostBaseSpeed,
            MovementRules.EffectiveSpeed(ghost, match));
    }

    [Fact]
    public void Hunter_is_strictly_slower_than_runner_so_a_tail_chase_can_never_close()
    {
        // SC-002: the whole balance premise. If this ever inverts, a direct chase wins and the
        // Ghost has no reason to predict or cut off.
        var match = TestMatch.OpenCorridor();

        var runnerSpeed = MovementRules.EffectiveSpeed(match.Pacman(), match);
        var hunterSpeed = MovementRules.EffectiveSpeed(match.Ghost(), match);

        Assert.True(hunterSpeed < runnerSpeed, $"hunter {hunterSpeed} not slower than runner {runnerSpeed}");
    }

    [Fact]
    public void Cornering_penalty_is_multiplicative_on_current_speed_not_a_flat_subtraction()
    {
        // Clarified 2026-08-14: 0.95 x 0.95 = 0.9025, NOT 0.95 - 0.05 = 0.90. The distinction
        // matters because the penalty has to compose with the frightened and anti-camping
        // modifiers rather than replace them.
        var match = TestMatch.OpenCorridor();
        var ghost = match.Ghost().At(2, 2).Heading(Direction.Right);
        ghost.CorneringPenaltyActive = true;

        var expected = BalanceConstants.Movement.GhostBaseSpeed
                       * BalanceConstants.Movement.GhostCorneringMultiplier;

        Assert.Equal(expected, MovementRules.EffectiveSpeed(ghost, match), precision: 10);
        Assert.Equal(0.9025, MovementRules.EffectiveSpeed(ghost, match), precision: 10);
    }

    [Fact]
    public void Runner_never_takes_a_cornering_penalty()
    {
        var match = TestMatch.OpenCorridor();
        var pacman = match.Pacman().At(2, 2).Heading(Direction.Right);
        pacman.CorneringPenaltyActive = true; // even if somehow flagged

        Assert.Equal(
            BalanceConstants.Movement.PacmanBaseSpeed,
            MovementRules.EffectiveSpeed(pacman, match));
    }

    [Fact]
    public void Player_advances_along_its_facing_direction()
    {
        var match = TestMatch.OpenCorridor();
        var pacman = match.Pacman().At(2, 2).Heading(Direction.Right);

        MovementRules.Advance(match, deltaMs: 100);

        Assert.True(pacman.X > 2, $"expected movement right from x=2, got {pacman.X}");
        Assert.Equal(2, pacman.Y, precision: 6);
    }

    [Fact]
    public void Movement_distance_matches_the_speed_ratio_between_roles()
    {
        var match = TestMatch.OpenCorridor();
        var pacman = match.Pacman().At(1, 1).Heading(Direction.Right);
        var ghost = match.Ghost().At(1, 3).Heading(Direction.Right);

        MovementRules.Advance(match, deltaMs: 100);

        var runnerDistance = pacman.X - 1;
        var hunterDistance = ghost.X - 1;

        Assert.Equal(
            BalanceConstants.Movement.GhostBaseSpeed,
            hunterDistance / runnerDistance,
            precision: 6);
    }

    [Fact]
    public void A_player_cannot_walk_through_a_wall()
    {
        var match = TestMatch.FromLayout(
            "#####",
            "#   #",
            "#####");
        var pacman = match.Pacman().At(3, 1).Heading(Direction.Right);

        // Far more than enough time to cross into the wall at x=4 if collision were not enforced.
        MovementRules.Advance(match, deltaMs: 2000);

        Assert.True(pacman.X <= 3.5, $"runner passed into the wall, x={pacman.X}");
    }

    [Fact]
    public void Turning_at_a_tile_center_changes_facing_without_a_penalty()
    {
        var match = TestMatch.OpenCorridor();
        var ghost = match.Ghost().At(2, 2).Heading(Direction.Right);
        ghost.DesiredDirection = Direction.Down;

        MovementRules.Advance(match, deltaMs: 10);

        Assert.Equal(Direction.Down, ghost.Facing);
        Assert.False(ghost.CorneringPenaltyActive, "a turn taken exactly at a tile center is not off-center");
    }

    [Fact]
    public void Runner_turn_input_is_buffered_and_applied_at_the_next_tile_center()
    {
        // FR-001: Pac-Man benefits from pre-buffered cornering - a turn queued before reaching the
        // intersection snaps cleanly onto the new axis with no speed loss.
        var match = TestMatch.OpenCorridor();
        var pacman = match.Pacman().At(2.4, 2).Heading(Direction.Right);
        pacman.DesiredDirection = Direction.Down;

        MovementRules.Advance(match, deltaMs: 250);

        Assert.Equal(Direction.Down, pacman.Facing);
        Assert.False(pacman.CorneringPenaltyActive);
    }
}
