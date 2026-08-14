using MatchServer.State;

namespace MatchServer.UnitTests;

/// <summary>
/// Builders for rule tests. Rules operate on plain state objects with no hub or networking
/// dependency (research.md §4), so a test can construct an exact board position and assert one
/// rule's behaviour in isolation.
/// </summary>
public static class TestMatch
{
    /// <summary>
    /// Builds a match from an ASCII board, using the same legend as FixedMap:
    /// '#' wall, '.' pellet, 'o' power pellet, ' ' empty, 'P' runner spawn, 'G' ghost house.
    /// </summary>
    public static MatchState FromLayout(params string[] layout)
    {
        var height = layout.Length;
        var width = layout[0].Length;

        var walls = new bool[height][];
        var pellets = new List<Pellet>();
        var powerPellets = new List<PowerPellet>();
        (int X, int Y) ghostHouse = (1, 1);
        (int X, int Y) runnerSpawn = (1, 1);

        for (var y = 0; y < height; y++)
        {
            walls[y] = new bool[width];
            for (var x = 0; x < width; x++)
            {
                switch (layout[y][x])
                {
                    case '#': walls[y][x] = true; break;
                    case '.': pellets.Add(new Pellet { X = x, Y = y }); break;
                    case 'o': powerPellets.Add(new PowerPellet { X = x, Y = y }); break;
                    case 'G': ghostHouse = (x, y); break;
                    case 'P': runnerSpawn = (x, y); break;
                }
            }
        }

        var map = new MapState
        {
            MapId = "test",
            Width = width,
            Height = height,
            Walls = walls,
            Pellets = pellets,
            PowerPellets = powerPellets,
            GhostHouse = ghostHouse,
            RunnerSpawn = runnerSpawn,
        };

        var match = new MatchState { MatchId = "test", Map = map, Status = MatchStatus.Active };
        match.Pacman = PlayerState.CreateRunner("runner-conn", runnerSpawn.X, runnerSpawn.Y);
        match.Ghost = PlayerState.CreateHunter("hunter-conn", ghostHouse.X, ghostHouse.Y);
        return match;
    }

    /// <summary>An open corridor with no pellets - the simplest board for movement assertions.</summary>
    public static MatchState OpenCorridor() => FromLayout(
        "###########",
        "#         #",
        "#         #",
        "#         #",
        "###########");

    public static PlayerState Pacman(this MatchState match) =>
        match.Pacman ?? throw new InvalidOperationException("test match has no runner");

    public static PlayerState Ghost(this MatchState match) =>
        match.Ghost ?? throw new InvalidOperationException("test match has no hunter");

    public static PlayerState At(this PlayerState player, double x, double y)
    {
        player.X = x;
        player.Y = y;
        return player;
    }

    public static PlayerState Heading(this PlayerState player, Direction direction)
    {
        player.Facing = direction;
        player.DesiredDirection = direction;
        return player;
    }
}
