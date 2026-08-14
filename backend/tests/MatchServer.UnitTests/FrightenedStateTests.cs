using MatchServer.Engine;
using MatchServer.Generated;
using MatchServer.State;

namespace MatchServer.UnitTests;

/// <summary>FR-005: an 8.0s Frightened window that RESETS rather than stacks.</summary>
public sealed class FrightenedStateTests
{
    private static MatchState BoardWithPowerPellet()
    {
        var match = TestMatch.FromLayout(
            "#######",
            "#o   P#",
            "#######");
        return match;
    }

    [Fact]
    public void Eating_a_power_pellet_opens_an_eight_second_window()
    {
        var match = BoardWithPowerPellet();
        match.ElapsedMs = 1_000;
        match.Pacman().At(1, 1);

        var result = PelletRules.Collect(match);
        Assert.True(result.PowerPelletEaten);

        PelletRules.StartOrResetFrightened(match);

        Assert.NotNull(match.Frightened);
        Assert.True(match.IsFrightenedActive);
        Assert.Equal(
            1_000 + BalanceConstants.Frightened.FrightenedDurationMs,
            match.Frightened!.ExpiresAtMs);
    }

    [Fact]
    public void The_window_expires_after_exactly_eight_seconds()
    {
        var match = BoardWithPowerPellet();
        match.ElapsedMs = 0;
        match.Frightened = new FrightenedState { StartedAtMs = 0 };

        match.ElapsedMs = BalanceConstants.Frightened.FrightenedDurationMs - 1;
        Assert.True(match.IsFrightenedActive);

        match.ElapsedMs = BalanceConstants.Frightened.FrightenedDurationMs;
        Assert.False(match.IsFrightenedActive);
    }

    [Fact]
    public void A_second_pellet_resets_the_timer_rather_than_extending_it()
    {
        // FR-005 is explicit that the window is non-stackable. Stacking would let Pac-Man bank a
        // 16-second window by eating two pellets back to back.
        var match = BoardWithPowerPellet();
        match.Frightened = new FrightenedState { StartedAtMs = 0 };

        match.ElapsedMs = 6_000; // 2s left
        PelletRules.StartOrResetFrightened(match);

        Assert.Equal(6_000, match.Frightened.StartedAtMs);
        Assert.Equal(
            6_000 + BalanceConstants.Frightened.FrightenedDurationMs,
            match.Frightened.ExpiresAtMs);
    }

    [Fact]
    public void Resetting_reports_that_a_window_was_already_active()
    {
        var match = BoardWithPowerPellet();
        match.ElapsedMs = 0;

        Assert.False(PelletRules.StartOrResetFrightened(match)); // first
        match.ElapsedMs = 2_000;
        Assert.True(PelletRules.StartOrResetFrightened(match)); // reset while active
    }

    [Fact]
    public void The_inversion_window_covers_the_first_three_seconds_only()
    {
        var match = BoardWithPowerPellet();
        match.Frightened = new FrightenedState { StartedAtMs = 0 };

        match.ElapsedMs = BalanceConstants.Frightened.FrightenedInversionMs - 1;
        Assert.True(match.IsInversionActive);

        match.ElapsedMs = BalanceConstants.Frightened.FrightenedInversionMs;
        Assert.False(match.IsInversionActive);

        // Spec Edge Case: inversion always resolves inside the 8s window, so it can never outlast
        // the frightened state itself.
        Assert.True(
            BalanceConstants.Frightened.FrightenedInversionMs
            < BalanceConstants.Frightened.FrightenedDurationMs);
    }

    [Fact]
    public void Starting_a_window_puts_a_normal_ghost_into_frightened()
    {
        var match = BoardWithPowerPellet();

        PelletRules.StartOrResetFrightened(match);

        Assert.Equal(GhostSubState.Frightened, match.Ghost().GhostSubState);
    }

    [Fact]
    public void A_new_window_starts_a_fresh_catch_chain()
    {
        var match = BoardWithPowerPellet();
        match.ScoreChain = 3;

        PelletRules.StartOrResetFrightened(match);

        Assert.Equal(0, match.ScoreChain);
    }

    [Fact]
    public void An_eyes_only_ghost_is_not_dragged_back_into_frightened_by_a_new_pellet()
    {
        // A ghost already heading home must complete its return-and-lockout, otherwise eating a
        // second pellet would rescue it from the trip.
        var match = BoardWithPowerPellet();
        match.Ghost().GhostSubState = GhostSubState.EyesOnly;

        PelletRules.StartOrResetFrightened(match);

        Assert.Equal(GhostSubState.EyesOnly, match.Ghost().GhostSubState);
    }
}
