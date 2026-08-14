using MatchServer.Generated;
using MatchServer.State;

namespace MatchServer.Engine;

/// <summary>
/// Asymmetric vision (FR-010, FR-011): Pac-Man sees everything, always; the Hunter sees a 6-tile
/// radius plus whatever lies down an unobstructed row or column.
/// </summary>
/// <remarks>
/// This is the rule the per-recipient state projection consults. Enforcing it by NOT SENDING the
/// Runner's position - rather than sending it and asking the client to hide it - is required by
/// Constitution Principle III (see contracts/match-room-protocol.md).
/// </remarks>
public static class VisionRules
{
    /// <summary>FR-010: absolute and unconditional. Pac-Man is never fogged.</summary>
    public static bool IsHunterVisibleToRunner(MatchState match) => true;

    /// <summary>FR-011: radius OR clear line of sight along a row/column.</summary>
    public static bool IsRunnerVisibleToHunter(MatchState match)
    {
        if (match.Pacman is not { } pacman || match.Ghost is not { } ghost)
        {
            return false;
        }

        return IsWithinRadius(pacman, ghost) || HasLineOfSight(match, pacman, ghost);
    }

    private static bool IsWithinRadius(PlayerState pacman, PlayerState ghost)
    {
        var dx = pacman.X - ghost.X;
        var dy = pacman.Y - ghost.Y;

        // Straight-line distance, so the radius is a circle rather than a diamond or a square -
        // "6-tile radius" reads as a radius.
        return Math.Sqrt(dx * dx + dy * dy) <= BalanceConstants.Vision.VisionRadiusTiles;
    }

    /// <summary>
    /// True when the two share a row or column with no wall between them.
    /// </summary>
    /// <remarks>
    /// Corridors only: a diagonal is not a line of sight. That keeps the rule legible to players -
    /// "if you can look straight down the hall at them, you see them" - rather than depending on
    /// how a raycast happens to clip a corner.
    /// </remarks>
    private static bool HasLineOfSight(MatchState match, PlayerState pacman, PlayerState ghost)
    {
        var px = (int)Math.Round(pacman.X);
        var py = (int)Math.Round(pacman.Y);
        var gx = (int)Math.Round(ghost.X);
        var gy = (int)Math.Round(ghost.Y);

        if (py == gy)
        {
            var from = Math.Min(px, gx) + 1;
            var to = Math.Max(px, gx);
            for (var x = from; x < to; x++)
            {
                if (match.Map.IsWall(x, py)) return false;
            }

            return true;
        }

        if (px == gx)
        {
            var from = Math.Min(py, gy) + 1;
            var to = Math.Max(py, gy);
            for (var y = from; y < to; y++)
            {
                if (match.Map.IsWall(px, y)) return false;
            }

            return true;
        }

        return false;
    }
}
