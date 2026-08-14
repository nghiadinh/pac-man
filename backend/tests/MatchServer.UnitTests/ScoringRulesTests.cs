using MatchServer.Engine;
using MatchServer.Generated;
using MatchServer.State;

namespace MatchServer.UnitTests;

/// <summary>FR-018 scoring matrix, and the FR-009 escalating catch chain.</summary>
public sealed class ScoringRulesTests
{
    [Fact]
    public void Regular_pellet_awards_ten_points_to_pacman()
    {
        var match = TestMatch.OpenCorridor();

        var evt = ScoringRules.Award(match, ScoreEventType.PelletCollected);

        Assert.Equal(BalanceConstants.Scoring.PelletPoints, evt.Points);
        Assert.Equal(10, evt.Points);
        Assert.Equal(Role.Runner, evt.Recipient);
        Assert.Equal(10, match.Pacman().Score);
    }

    [Fact]
    public void Power_pellet_awards_fifty_points_to_pacman()
    {
        var match = TestMatch.OpenCorridor();

        var evt = ScoringRules.Award(match, ScoreEventType.PowerPelletCollected);

        Assert.Equal(50, evt.Points);
        Assert.Equal(Role.Runner, evt.Recipient);
    }

    [Fact]
    public void Eliminating_pacman_awards_five_hundred_points_to_the_ghost()
    {
        var match = TestMatch.OpenCorridor();

        var evt = ScoringRules.Award(match, ScoreEventType.PacmanEliminated);

        Assert.Equal(500, evt.Points);
        Assert.Equal(Role.Hunter, evt.Recipient);
        Assert.Equal(500, match.Ghost().Score);
        Assert.Equal(0, match.Pacman().Score);
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(1, 400)]
    [InlineData(2, 800)]
    [InlineData(3, 1600)]
    public void Ghost_catches_escalate_through_the_chain(int chainIndex, int expectedPoints)
    {
        var match = TestMatch.OpenCorridor();
        match.ScoreChain = chainIndex;

        var evt = ScoringRules.Award(match, ScoreEventType.GhostCaught);

        Assert.Equal(expectedPoints, evt.Points);
        Assert.Equal(Role.Runner, evt.Recipient);
    }

    [Fact]
    public void Chain_beyond_the_fourth_catch_stays_at_the_top_value()
    {
        // FR-009 defines four steps. A fifth catch in one window is only reachable with
        // overlapping frightened windows, and must not index past the end of the table.
        var match = TestMatch.OpenCorridor();
        match.ScoreChain = 7;

        var evt = ScoringRules.Award(match, ScoreEventType.GhostCaught);

        Assert.Equal(1600, evt.Points);
    }

    [Fact]
    public void Time_bonus_is_five_points_per_whole_second_remaining()
    {
        var match = TestMatch.OpenCorridor();
        match.ElapsedMs = BalanceConstants.Match.MatchDurationMs - 10_000; // 10s left

        var evt = ScoringRules.AwardTimeBonus(match);

        Assert.Equal(50, evt.Points);
        Assert.Equal(Role.Runner, evt.Recipient);
        Assert.Equal(50, match.Pacman().Score);
    }

    [Fact]
    public void Scores_accumulate_across_multiple_events()
    {
        var match = TestMatch.OpenCorridor();

        ScoringRules.Award(match, ScoreEventType.PelletCollected);
        ScoringRules.Award(match, ScoreEventType.PelletCollected);
        ScoringRules.Award(match, ScoreEventType.PowerPelletCollected);

        Assert.Equal(70, match.Pacman().Score);
    }
}
