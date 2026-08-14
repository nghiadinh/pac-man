using MatchServer.Engine;
using MatchServer.Generated;
using MatchServer.State;

namespace MatchServer.UnitTests;

/// <summary>
/// FR-012: parking within 3 tiles of an uncollected Power Pellet for more than 5.0 seconds while
/// Pac-Man is out of sight costs the Hunter an extra 15% speed, until it leaves the zone.
/// </summary>
public sealed class AntiCampingRulesTests
{
    /// <summary>Power pellet at (1,1); Pac-Man parked far away and out of sight by default.</summary>
    private static MatchState Board()
    {
        var match = TestMatch.FromLayout(
            "###############",
            "#o            #",
            "#             #",
            "#      #      #",
            "#             #",
            "#            P#",
            "###############");
        match.Pacman().At(13, 5);
        return match;
    }

    private static void Tick(MatchState match, double ms) => AntiCampingRules.Advance(match, ms);

    [Fact]
    public void Loitering_in_the_zone_past_five_seconds_applies_the_debuff()
    {
        var match = Board();
        match.Ghost().At(2, 1); // within 3 tiles of the pellet

        Tick(match, BalanceConstants.AntiCamping.CampTriggerMs + 1);

        Assert.True(AntiCampingRules.IsDebuffActive(match));
    }

    [Fact]
    public void The_debuff_does_not_apply_before_the_threshold()
    {
        var match = Board();
        match.Ghost().At(2, 1);

        Tick(match, BalanceConstants.AntiCamping.CampTriggerMs - 100);

        Assert.False(AntiCampingRules.IsDebuffActive(match));
    }

    [Fact]
    public void Standing_outside_the_zone_never_triggers_it()
    {
        var match = Board();
        match.Ghost().At(10, 4); // well beyond 3 tiles

        Tick(match, 20_000);

        Assert.False(AntiCampingRules.IsDebuffActive(match));
    }

    [Fact]
    public void Leaving_the_zone_clears_the_debuff_and_resets_the_timer()
    {
        var match = Board();
        match.Ghost().At(2, 1);
        Tick(match, BalanceConstants.AntiCamping.CampTriggerMs + 1);
        Assert.True(AntiCampingRules.IsDebuffActive(match));

        match.Ghost().At(10, 4);
        Tick(match, 33);

        Assert.False(AntiCampingRules.IsDebuffActive(match));

        // Re-entering starts from zero rather than resuming a banked timer.
        match.Ghost().At(2, 1);
        Tick(match, 1_000);
        Assert.False(AntiCampingRules.IsDebuffActive(match));
    }

    [Fact]
    public void The_timer_resets_whenever_pacman_becomes_visible()
    {
        // Clarified 2026-08-14: a Hunter whose target is right there is chasing, not camping.
        var match = Board();
        match.Ghost().At(2, 1);

        Tick(match, 4_000); // nearly at the threshold

        match.Pacman().At(3, 1); // now well inside the vision radius
        Tick(match, 33);

        // Even well past the original threshold, visibility keeps resetting the timer.
        Tick(match, 10_000);
        Assert.False(AntiCampingRules.IsDebuffActive(match));
    }

    [Fact]
    public void Collecting_the_pellet_clears_the_zone_entirely()
    {
        // The trigger requires an UNCOLLECTED power pellet, so eating the last one in a zone must
        // release a Hunter already being penalised for it.
        var match = Board();
        match.Ghost().At(2, 1);
        Tick(match, BalanceConstants.AntiCamping.CampTriggerMs + 1);
        Assert.True(AntiCampingRules.IsDebuffActive(match));

        match.Map.PowerPellets[0].Collected = true;
        Tick(match, 33);

        Assert.False(AntiCampingRules.IsDebuffActive(match));
    }

    [Fact]
    public void The_debuff_reduces_speed_by_a_further_fifteen_percent()
    {
        var match = Board();
        match.Ghost().At(2, 1);
        Tick(match, BalanceConstants.AntiCamping.CampTriggerMs + 1);

        var expected = BalanceConstants.Movement.GhostBaseSpeed
                       * (1 - BalanceConstants.AntiCamping.CampSpeedPenalty);

        Assert.Equal(expected, MovementRules.EffectiveSpeed(match.Ghost(), match), precision: 10);
        Assert.Equal(0.8075, MovementRules.EffectiveSpeed(match.Ghost(), match), precision: 10);
    }

    [Fact]
    public void The_debuff_becomes_observable_within_a_second_of_the_threshold()
    {
        // SC-004: visible and measurable within 1 second of crossing the line, every time.
        var match = Board();
        match.Ghost().At(2, 1);

        var elapsed = 0.0;
        while (elapsed < BalanceConstants.AntiCamping.CampTriggerMs + 1_000)
        {
            Tick(match, 33);
            elapsed += 33;

            if (elapsed > BalanceConstants.AntiCamping.CampTriggerMs + 1_000)
            {
                break;
            }
        }

        Assert.True(
            AntiCampingRules.IsDebuffActive(match),
            "debuff was not observable within 1s of the threshold");
    }

    [Fact]
    public void A_debuffed_ghost_is_slower_than_pacman_by_a_wide_margin()
    {
        var match = Board();
        match.Ghost().At(2, 1);
        Tick(match, BalanceConstants.AntiCamping.CampTriggerMs + 1);

        var hunter = MovementRules.EffectiveSpeed(match.Ghost(), match);
        var runner = MovementRules.EffectiveSpeed(match.Pacman(), match);

        Assert.True(hunter < runner * 0.85);
    }

    [Fact]
    public void Eyes_returning_home_are_not_penalised_for_passing_a_power_pellet()
    {
        // Eyes are server-steered and cannot choose their route, so penalising them would punish
        // the Hunter for something it does not control.
        var match = Board();
        match.Ghost().At(2, 1);
        match.Ghost().GhostSubState = GhostSubState.EyesOnly;

        Tick(match, BalanceConstants.AntiCamping.CampTriggerMs + 1);

        Assert.Equal(
            BalanceConstants.Movement.EyesSpeed,
            MovementRules.EffectiveSpeed(match.Ghost(), match));
    }
}
