namespace MatchServer.State;

/// <summary>A regular pellet worth 10 points (FR-018). Counts toward the 70%/100% thresholds.</summary>
public class Pellet
{
    public required int X { get; init; }

    public required int Y { get; init; }

    /// <summary>Set once when the Runner occupies the tile. Irreversible for the match.</summary>
    public bool Collected { get; set; }
}

/// <summary>
/// A Power Pellet worth 50 points that triggers the Frightened State (FR-005).
/// Also carries the FR-012 anti-camping zone timer for its own 3-tile radius.
/// </summary>
public sealed class PowerPellet : Pellet
{
    /// <summary>
    /// Continuous ms the Hunter has spent inside this pellet's camp radius while the Runner was
    /// NOT visible. Resets to zero when the Hunter leaves, when the Runner becomes visible
    /// (FR-012, clarified 2026-08-14), or when the pellet is collected.
    /// </summary>
    public double CampTimerMs { get; set; }

    /// <summary>Whether this pellet's zone is the one currently applying the debuff.</summary>
    public bool CampDebuffActive { get; set; }
}

/// <summary>
/// The single fixed map (FR-022). Walls are immutable for the match; pellet collection state is not.
/// </summary>
public sealed class MapState
{
    public required string MapId { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>[y][x] - true where a wall blocks movement and line of sight.</summary>
    public required bool[][] Walls { get; init; }

    public required List<Pellet> Pellets { get; init; }

    public required List<PowerPellet> PowerPellets { get; init; }

    public required (int X, int Y) GhostHouse { get; init; }

    public required (int X, int Y) RunnerSpawn { get; init; }

    /// <summary>Denominator for the FR-015 100% and FR-017 70% clear thresholds.</summary>
    public int TotalPelletCount => Pellets.Count + PowerPellets.Count;

    public int CollectedPelletCount =>
        Pellets.Count(p => p.Collected) + PowerPellets.Count(p => p.Collected);

    public double ClearedFraction =>
        TotalPelletCount == 0 ? 1.0 : (double)CollectedPelletCount / TotalPelletCount;

    public bool IsWall(int x, int y) =>
        x < 0 || y < 0 || x >= Width || y >= Height || Walls[y][x];

    public bool IsWalkable(int x, int y) => !IsWall(x, y);
}
