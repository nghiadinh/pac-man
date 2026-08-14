using MatchServer.Generated;
using MatchServer.State;

namespace MatchServer.Engine;

/// <summary>
/// The Hunter's sub-state machine (data-model.md):
/// Normal → Frightened (FR-005) → EyesOnly (FR-009) → Respawning → Normal,
/// plus Frightened → Normal directly when the 8.0s window lapses uncaught.
/// </summary>
/// <remarks>
/// Every transition lives here rather than being scattered across the rules that trigger them,
/// so the legal set of transitions can be read - and tested - in one place. That matters for
/// Constitution Principle II: a missed case is how a ghost ends up in a state nothing can move it
/// out of, mid-match.
/// </remarks>
public static class GhostStateMachine
{
    /// <summary>Advances timers and state transitions for the current tick.</summary>
    public static void Advance(MatchState match)
    {
        if (match.Ghost is not { } ghost)
        {
            return;
        }

        switch (ghost.GhostSubState)
        {
            case GhostSubState.Frightened:
                ExpireFrightened(match, ghost);
                break;

            case GhostSubState.EyesOnly:
                SteerHome(match, ghost);
                break;

            case GhostSubState.Respawning:
                ReleaseIfReady(match, ghost);
                break;

            case GhostSubState.Normal:
                break;
        }
    }

    /// <summary>FR-005: the window lapsing returns the ghost to the hunt.</summary>
    private static void ExpireFrightened(MatchState match, PlayerState ghost)
    {
        if (match.IsFrightenedActive)
        {
            return;
        }

        ghost.GhostSubState = GhostSubState.Normal;
        match.Frightened = null;

        // A chain only counts within one unbroken window (FR-009).
        match.ScoreChain = 0;
    }

    /// <summary>
    /// FR-009: eyes travel back to the Ghost House, then serve a 5.0s lockout before re-release.
    /// </summary>
    private static void SteerHome(MatchState match, PlayerState ghost)
    {
        var (houseX, houseY) = match.Map.GhostHouse;

        if (HasArrived(ghost, houseX, houseY))
        {
            ghost.X = houseX;
            ghost.Y = houseY;
            ghost.Facing = Direction.None;
            ghost.DesiredDirection = Direction.None;
            ghost.CorneringPenaltyActive = false;

            ghost.GhostSubState = GhostSubState.Respawning;
            ghost.RespawnReadyAtMs =
                match.ElapsedMs + BalanceConstants.Frightened.GhostHouseLockoutMs;
            return;
        }

        // Eyes are server-steered, not player-steered - the Hunter has no control of the trip home.
        ghost.DesiredDirection = StepToward(match, ghost, houseX, houseY);
    }

    private static void ReleaseIfReady(MatchState match, PlayerState ghost)
    {
        if (ghost.RespawnReadyAtMs is not { } readyAt || match.ElapsedMs < readyAt)
        {
            return;
        }

        ghost.GhostSubState = GhostSubState.Normal;
        ghost.RespawnReadyAtMs = null;
    }

    private static bool HasArrived(PlayerState ghost, int houseX, int houseY) =>
        Math.Abs(ghost.X - houseX) < 0.25 && Math.Abs(ghost.Y - houseY) < 0.25;

    /// <summary>
    /// Breadth-first step toward the ghost house.
    /// </summary>
    /// <remarks>
    /// A greedy "reduce the axis distance" heuristic gets eyes stuck against concave walls, which
    /// would strand the ghost outside its house forever and hang the match in a state the Hunter
    /// cannot escape. The maze is small enough that a BFS per tick is negligible and always
    /// correct.
    /// </remarks>
    private static Direction StepToward(MatchState match, PlayerState ghost, int targetX, int targetY)
    {
        var startX = (int)Math.Round(ghost.X);
        var startY = (int)Math.Round(ghost.Y);

        if (startX == targetX && startY == targetY)
        {
            return Direction.None;
        }

        var map = match.Map;
        var visited = new bool[map.Height, map.Width];
        var firstStep = new Direction[map.Height, map.Width];
        var queue = new Queue<(int X, int Y)>();

        visited[startY, startX] = true;
        queue.Enqueue((startX, startY));

        Direction[] directions = [Direction.Up, Direction.Down, Direction.Left, Direction.Right];

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();

            foreach (var direction in directions)
            {
                var (dx, dy) = direction.Delta();
                var nx = x + dx;
                var ny = y + dy;

                if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height) continue;
                if (visited[ny, nx] || map.IsWall(nx, ny)) continue;

                visited[ny, nx] = true;
                // Carry the first move of this path so the answer is a single direction.
                firstStep[ny, nx] = x == startX && y == startY ? direction : firstStep[y, x];

                if (nx == targetX && ny == targetY)
                {
                    return firstStep[ny, nx];
                }

                queue.Enqueue((nx, ny));
            }
        }

        return Direction.None; // unreachable - the map validates connectivity, so this is inert
    }
}
