namespace MatchServer.State;

/// <summary>The two fixed roles. Assigned on join and never swapped mid-match (spec Assumptions).</summary>
public enum Role
{
    Runner,
    Hunter,
}

/// <summary>
/// Match lifecycle. <see cref="Ended"/> is terminal - reached by exactly one of FR-015 (pellets
/// cleared), FR-016 (lives depleted), FR-017 (timeout evaluation), or FR-020 (forfeit).
/// </summary>
public enum MatchStatus
{
    WaitingForPlayers,
    Active,
    Ended,
}

/// <summary>
/// Hunter sub-state machine (data-model.md). Transitions:
/// Normal -> Frightened (FR-005) -> EyesOnly (FR-009) -> Respawning -> Normal,
/// plus Frightened -> Normal directly when the 8.0s window elapses uncaught.
/// </summary>
public enum GhostSubState
{
    Normal,
    Frightened,
    EyesOnly,
    Respawning,
}

/// <summary>Grid-aligned facing. <see cref="None"/> means "no held input" (contract SendInput).</summary>
public enum Direction
{
    None,
    Up,
    Down,
    Left,
    Right,
}

/// <summary>Rows of the FR-018 scoring matrix.</summary>
public enum ScoreEventType
{
    PelletCollected,
    PowerPelletCollected,
    GhostCaught,
    PacmanEliminated,
    TimeBonus,
}

/// <summary>Maps 1:1 to FR-015 / FR-016 / FR-017 (+FR-023) / FR-020.</summary>
public enum MatchEndReason
{
    PelletsCleared,
    LivesDepleted,
    TimeoutClearThresholdMet,
    TimeoutClearThresholdMissed,
    Forfeit,
}

/// <summary>Map-relative quadrant for the sonar pulse (FR-011). Never Hunter-relative.</summary>
public enum Quadrant
{
    NE,
    NW,
    SE,
    SW,
}

public static class DirectionExtensions
{
    /// <summary>Inverts a direction. Used for the FR-007 frightened input inversion, server-side only.</summary>
    public static Direction Invert(this Direction direction) => direction switch
    {
        Direction.Up => Direction.Down,
        Direction.Down => Direction.Up,
        Direction.Left => Direction.Right,
        Direction.Right => Direction.Left,
        Direction.None => Direction.None,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "unhandled direction"),
    };

    /// <summary>Unit delta in tile coordinates. Y grows downward, matching the map grid.</summary>
    public static (int Dx, int Dy) Delta(this Direction direction) => direction switch
    {
        Direction.Up => (0, -1),
        Direction.Down => (0, 1),
        Direction.Left => (-1, 0),
        Direction.Right => (1, 0),
        Direction.None => (0, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "unhandled direction"),
    };
}
