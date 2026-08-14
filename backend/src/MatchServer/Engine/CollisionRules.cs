using MatchServer.Generated;
using MatchServer.State;

namespace MatchServer.Engine;

/// <summary>Outcome of one tick's collision resolution.</summary>
public sealed record CollisionResult
{
    public bool PacmanEliminated { get; init; }

    public bool GhostCaught { get; init; }

    public IReadOnlyList<ScoreEvent> ScoreEvents { get; init; } = [];

    public static readonly CollisionResult None = new();
}

/// <summary>
/// Contact between the two players (FR-002, FR-003, FR-004), including the FR-021 rule that a
/// normal-state elimination resolves BEFORE a same-tick Power Pellet pickup.
/// </summary>
public static class CollisionRules
{
    /// <summary>
    /// Resolves contact for this tick.
    /// </summary>
    /// <remarks>
    /// Called before pellet pickups in the tick pipeline. That ordering IS the FR-021 rule:
    /// eating a Power Pellet is a preemptive move, never a last-instant save, so a Runner who
    /// touches a normal-state Ghost on the same tick still loses the life.
    /// </remarks>
    public static CollisionResult Resolve(MatchState match)
    {
        if (match.Status != MatchStatus.Active)
        {
            return CollisionResult.None;
        }

        if (match.Pacman is not { } pacman || match.Ghost is not { } ghost)
        {
            return CollisionResult.None;
        }

        if (!AreTouching(pacman, ghost))
        {
            return CollisionResult.None;
        }

        // A ghost on its way home or waiting out a respawn is not a threat and not a target.
        if (ghost.GhostSubState is GhostSubState.EyesOnly or GhostSubState.Respawning)
        {
            return CollisionResult.None;
        }

        return ghost.GhostSubState == GhostSubState.Frightened
            ? CatchGhost(match, ghost)
            : EliminatePacman(match, pacman, ghost);
    }

    /// <summary>FR-004: matching 0.8x0.8 tile boxes, so contact is symmetric.</summary>
    public static bool AreTouching(PlayerState a, PlayerState b)
    {
        var box = BalanceConstants.Match.CollisionBoxTiles;
        return Math.Abs(a.X - b.X) < box && Math.Abs(a.Y - b.Y) < box;
    }

    private static CollisionResult EliminatePacman(
        MatchState match, PlayerState pacman, PlayerState ghost)
    {
        pacman.LivesRemaining--;

        var scoreEvent = ScoringRules.Award(match, ScoreEventType.PacmanEliminated);

        // Reset Pac-Man to its spawn tile so the next life starts from a known position.
        pacman.X = match.Map.RunnerSpawn.X;
        pacman.Y = match.Map.RunnerSpawn.Y;
        pacman.Facing = Direction.None;
        pacman.DesiredDirection = Direction.None;

        // FR-003: the Ghost sits out 5 seconds, which is what stops it camping the respawn tile
        // and instantly re-killing.
        ghost.GhostSubState = GhostSubState.Respawning;
        ghost.RespawnReadyAtMs = match.ElapsedMs + BalanceConstants.Match.GhostRespawnDelayMs;
        ghost.X = match.Map.GhostHouse.X;
        ghost.Y = match.Map.GhostHouse.Y;
        ghost.Facing = Direction.None;
        ghost.DesiredDirection = Direction.None;
        ghost.CorneringPenaltyActive = false;

        return new CollisionResult
        {
            PacmanEliminated = true,
            ScoreEvents = [scoreEvent],
        };
    }

    /// <summary>
    /// FR-009: Pac-Man catches a frightened Ghost. Points escalate through the chain, then the
    /// Ghost becomes eyes-only and heads home. Arrives fully with User Story 2.
    /// </summary>
    private static CollisionResult CatchGhost(MatchState match, PlayerState ghost)
    {
        var scoreEvent = ScoringRules.Award(match, ScoreEventType.GhostCaught);
        match.ScoreChain++;

        ghost.GhostSubState = GhostSubState.EyesOnly;
        ghost.CorneringPenaltyActive = false;

        return new CollisionResult
        {
            GhostCaught = true,
            ScoreEvents = [scoreEvent],
        };
    }
}
