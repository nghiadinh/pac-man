using MatchServer.Engine;
using MatchServer.Generated;
using MatchServer.State;

namespace MatchServer.UnitTests;

/// <summary>
/// FR-011 sonar: a pulse every 4.0s while the Runner is out of sight, carrying only a MAP-RELATIVE
/// quadrant (clarified 2026-08-14) and never exact coordinates.
/// </summary>
public sealed class SonarRulesTests
{
    /// <summary>A 12x12 open room, so the midlines fall at x=6 and y=6.</summary>
    private static MatchState Room()
    {
        var rows = new List<string> { new('#', 12) };
        for (var i = 0; i < 10; i++) rows.Add("#" + new string(' ', 10) + "#");
        rows.Add(new string('#', 12));
        return TestMatch.FromLayout([.. rows]);
    }

    [Theory]
    [InlineData(2, 2, Quadrant.NW)]
    [InlineData(9, 2, Quadrant.NE)]
    [InlineData(2, 9, Quadrant.SW)]
    [InlineData(9, 9, Quadrant.SE)]
    public void Quadrant_is_resolved_against_the_map_midlines(int x, int y, Quadrant expected)
    {
        var match = Room();
        match.Pacman().At(x, y);

        Assert.Equal(expected, SonarRules.QuadrantOf(match));
    }

    [Fact]
    public void Quadrant_is_map_relative_not_hunter_relative()
    {
        // The clarification that matters: a Hunter-relative quadrant would be a bearing to target,
        // which would gut the vision disadvantage FR-011 exists to create. Moving the Hunter must
        // therefore change nothing.
        var match = Room();
        match.Pacman().At(2, 2);

        match.Ghost().At(1, 1);
        var fromNorthWest = SonarRules.QuadrantOf(match);

        match.Ghost().At(10, 10);
        var fromSouthEast = SonarRules.QuadrantOf(match);

        Assert.Equal(fromNorthWest, fromSouthEast);
        Assert.Equal(Quadrant.NW, fromNorthWest);
    }

    [Fact]
    public void The_first_pulse_is_due_immediately_so_the_hunter_is_never_left_blind_at_the_start()
    {
        var match = Room();
        match.ElapsedMs = 0;

        Assert.True(SonarRules.IsPulseDue(match, lastPulseAtMs: null));
    }

    [Fact]
    public void Pulses_are_spaced_four_seconds_apart()
    {
        var match = Room();
        var interval = BalanceConstants.Vision.SonarIntervalMs;

        match.ElapsedMs = interval - 1;
        Assert.False(SonarRules.IsPulseDue(match, lastPulseAtMs: 0));

        match.ElapsedMs = interval;
        Assert.True(SonarRules.IsPulseDue(match, lastPulseAtMs: 0));
    }

    [Fact]
    public void A_pulse_carries_no_coordinates()
    {
        // The payload is an enum by construction, so there is nothing to leak - this test pins
        // that down so a future "helpful" addition of x/y has to break it deliberately.
        var match = Room();
        match.Pacman().At(3, 8);

        var quadrant = SonarRules.QuadrantOf(match);

        Assert.IsType<Quadrant>(quadrant);
        Assert.Equal(Quadrant.SW, quadrant);
    }

    [Fact]
    public void Quadrant_resolution_survives_a_player_sitting_exactly_on_a_midline()
    {
        var match = Room();
        match.Pacman().At(6, 6); // exactly on both midlines

        var quadrant = SonarRules.QuadrantOf(match);

        // Any consistent answer is fine; what must not happen is a crash or an undefined value.
        Assert.Contains(quadrant, new[] { Quadrant.NE, Quadrant.NW, Quadrant.SE, Quadrant.SW });
    }
}
