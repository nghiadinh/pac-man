using MatchServer.Engine;
using MatchServer.Generated;
using MatchServer.State;

namespace MatchServer.UnitTests;

/// <summary>
/// FR-010 (Pac-Man always sees everything) and FR-011 (the Hunter sees a 6-tile radius plus
/// unobstructed line of sight).
/// </summary>
public sealed class VisionRulesTests
{
    /// <summary>A long open hall, so distance is the only thing limiting sight.</summary>
    private static MatchState OpenHall() => TestMatch.FromLayout(
        "###############",
        "#             #",
        "#             #",
        "#             #",
        "###############");

    [Fact]
    public void Pacman_is_visible_when_well_inside_the_radius()
    {
        var match = OpenHall();
        match.Ghost().At(2, 2);
        match.Pacman().At(5, 2);

        Assert.True(VisionRules.IsRunnerVisibleToHunter(match));
    }

    [Fact]
    public void Pacman_is_visible_at_exactly_the_radius_boundary()
    {
        var match = OpenHall();
        match.Ghost().At(2, 2);
        match.Pacman().At(2 + BalanceConstants.Vision.VisionRadiusTiles, 2);

        Assert.True(VisionRules.IsRunnerVisibleToHunter(match));
    }

    [Fact]
    public void Pacman_is_hidden_beyond_the_radius_when_out_of_line_of_sight()
    {
        var match = TestMatch.FromLayout(
            "###############",
            "#      #      #",
            "#      #      #",
            "#      #      #",
            "###############");

        match.Ghost().At(1, 2);
        match.Pacman().At(13, 2); // 12 tiles away and behind the central wall

        Assert.False(VisionRules.IsRunnerVisibleToHunter(match));
    }

    [Fact]
    public void An_unobstructed_corridor_reveals_pacman_beyond_the_radius()
    {
        // FR-011: line of sight is additive to the radius - a Hunter looking down a clear hallway
        // sees all the way along it, which is what makes corridors dangerous for the Runner.
        var match = OpenHall();
        match.Ghost().At(1, 2);
        match.Pacman().At(13, 2); // 12 tiles, but the row is completely clear

        Assert.True(VisionRules.IsRunnerVisibleToHunter(match));
    }

    [Fact]
    public void A_wall_in_the_corridor_blocks_line_of_sight()
    {
        var match = TestMatch.FromLayout(
            "###############",
            "#             #",
            "#      #      #",
            "#             #",
            "###############");

        match.Ghost().At(1, 2);
        match.Pacman().At(13, 2); // same row, but a wall sits between them

        Assert.False(VisionRules.IsRunnerVisibleToHunter(match));
    }

    [Fact]
    public void Line_of_sight_works_vertically_too()
    {
        var match = TestMatch.FromLayout(
            "#####",
            "#   #",
            "#   #",
            "#   #",
            "#   #",
            "#   #",
            "#   #",
            "#   #",
            "#   #",
            "#####");

        match.Ghost().At(2, 1);
        match.Pacman().At(2, 8);

        Assert.True(VisionRules.IsRunnerVisibleToHunter(match));
    }

    [Fact]
    public void Diagonal_separation_beyond_the_radius_is_not_line_of_sight()
    {
        // Sight is straight down rows and columns only - a diagonal is neither.
        var match = OpenHall();
        match.Ghost().At(1, 1);
        match.Pacman().At(13, 3);

        Assert.False(VisionRules.IsRunnerVisibleToHunter(match));
    }

    [Fact]
    public void Radius_is_measured_by_straight_line_distance_not_path_length()
    {
        var match = OpenHall();
        match.Ghost().At(2, 1);
        match.Pacman().At(6, 3); // ~4.47 tiles apart, comfortably inside 6

        Assert.True(VisionRules.IsRunnerVisibleToHunter(match));
    }

    [Fact]
    public void Vision_resolves_false_rather_than_throwing_when_a_player_is_missing()
    {
        var match = OpenHall();
        match.Ghost = null;

        Assert.False(VisionRules.IsRunnerVisibleToHunter(match));
    }

    [Fact]
    public void The_runner_is_never_subject_to_fog_of_war()
    {
        // FR-010 is absolute: there is no condition under which Pac-Man's view is restricted.
        var match = OpenHall();
        match.Ghost().At(1, 1);
        match.Pacman().At(13, 3);

        Assert.True(VisionRules.IsHunterVisibleToRunner(match));
    }
}
