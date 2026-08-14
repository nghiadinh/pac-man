namespace MatchServer.State;

/// <summary>
/// The single fixed map for this feature (FR-022). Multi-map support is explicitly out of scope,
/// so the layout is a compile-time constant rather than data loaded at runtime.
/// </summary>
public static class FixedMap
{
    public const string MapId = "classic-1v1";

    /// <summary>
    /// Legend:
    ///   '#' wall
    ///   '.' regular pellet (10 pts)
    ///   'o' power pellet (50 pts)
    ///   ' ' empty walkable tile (no pellet)
    ///   'G' ghost house / Hunter spawn
    ///   'P' Runner spawn
    /// The four power pellets sit in the corners, which is what makes the FR-012 anti-camping
    /// rule matter - they are the natural chokepoints a Hunter would otherwise park on.
    /// </summary>
    private static readonly string[] Layout =
    [
        "############################",
        "#o...........##...........o#",
        "#.####.#####.##.#####.####.#",
        "#.####.#####.##.#####.####.#",
        "#.####.#####.##.#####.####.#",
        "#..........................#",
        "#.####.##.########.##.####.#",
        "#......##....##....##......#",
        "######.##### ## #####.######",
        "#....#.##          ##.#....#",
        "#.####.## ###GG### ##.####.#",
        "#.........#      #.........#",
        "#.####.## #      # ##.####.#",
        "#....#.## ######## ##.#....#",
        "######.##          ##.######",
        "#............##............#",
        "#.####.#####.##.#####.####.#",
        "#.####.#####.##.#####.####.#",
        "#o..##.......P .......##..o#",
        "###.##.##.########.##.##.###",
        "#......##....##....##......#",
        "#.##########.##.##########.#",
        "#..........................#",
        "############################",
    ];

    public static MapState Create()
    {
        var height = Layout.Length;
        var width = Layout[0].Length;

        var walls = new bool[height][];
        var pellets = new List<Pellet>();
        var powerPellets = new List<PowerPellet>();
        (int X, int Y)? ghostHouse = null;
        (int X, int Y)? runnerSpawn = null;

        for (var y = 0; y < height; y++)
        {
            var row = Layout[y];
            if (row.Length != width)
            {
                throw new InvalidOperationException(
                    $"FixedMap row {y} is {row.Length} tiles wide; expected {width}. " +
                    "Every row must be the same width or wall lookups go out of bounds.");
            }

            walls[y] = new bool[width];

            for (var x = 0; x < width; x++)
            {
                switch (row[x])
                {
                    case '#':
                        walls[y][x] = true;
                        break;
                    case '.':
                        pellets.Add(new Pellet { X = x, Y = y });
                        break;
                    case 'o':
                        powerPellets.Add(new PowerPellet { X = x, Y = y });
                        break;
                    case 'G':
                        ghostHouse = (x, y);
                        break;
                    case 'P':
                        runnerSpawn = (x, y);
                        break;
                    case ' ':
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"FixedMap has unrecognised tile '{row[x]}' at ({x},{y}).");
                }
            }
        }

        if (ghostHouse is null)
        {
            throw new InvalidOperationException("FixedMap is missing a ghost house ('G') tile.");
        }

        if (runnerSpawn is null)
        {
            throw new InvalidOperationException("FixedMap is missing a runner spawn ('P') tile.");
        }

        return new MapState
        {
            MapId = MapId,
            Width = width,
            Height = height,
            Walls = walls,
            Pellets = pellets,
            PowerPellets = powerPellets,
            GhostHouse = ghostHouse.Value,
            RunnerSpawn = runnerSpawn.Value,
        };
    }
}
