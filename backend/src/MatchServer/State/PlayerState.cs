using MatchServer.Generated;

namespace MatchServer.State;

/// <summary>
/// One connected human occupying one of the two fixed roles (data-model.md).
/// Every field here is server-owned: the client sends input intent only and never
/// reports its own position, speed, or score (Constitution Principle III).
/// </summary>
public sealed class PlayerState
{
    public required string ConnectionId { get; set; }

    public required Role Role { get; init; }

    /// <summary>Authoritative position in tile coordinates (fractional between tile centers).</summary>
    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>Direction currently being travelled.</summary>
    public Direction Facing { get; set; } = Direction.None;

    /// <summary>Latest validated input intent. For the Hunter this is the player's TRUE intent;
    /// the FR-007 inversion is applied when the intent is consumed, not when it is stored.</summary>
    public Direction DesiredDirection { get; set; } = Direction.None;

    /// <summary>Runner only. Starts at 3 (FR-002); match ends the instant it reaches 0.</summary>
    public int LivesRemaining { get; set; }

    /// <summary>Hunter only. The Runner is always <see cref="GhostSubState.Normal"/>.</summary>
    public GhostSubState GhostSubState { get; set; } = GhostSubState.Normal;

    /// <summary>Hunter only. Match-relative ms gating a return to <see cref="GhostSubState.Normal"/>.</summary>
    public long? RespawnReadyAtMs { get; set; }

    /// <summary>Flips false on socket drop, which triggers the FR-020 forfeit path.</summary>
    public bool Connected { get; set; } = true;

    /// <summary>Real-time accumulated score per FR-018.</summary>
    public int Score { get; set; }

    /// <summary>
    /// Effective speed for the current tick. Derived and rewritten by the rules pipeline each
    /// tick - never set from client input (data-model.md; Constitution Principle III).
    /// Runner is always 1.00 (FR-001); Hunter is 0.95 normal, 0.80 anti-camping (FR-012),
    /// 0.70 frightened (FR-006), or 1.50 eyes-only (FR-009), further multiplied by the 0.95
    /// cornering penalty while mid-traversal after an off-center turn (FR-001).
    /// </summary>
    public double SpeedMultiplier { get; set; }

    /// <summary>
    /// True while the player is mid-traversal between tile centers and cornered off-center this
    /// traversal. Drives the FR-001 multiplicative cornering penalty, which lasts only until the
    /// next tile center is reached.
    /// </summary>
    public bool CorneringPenaltyActive { get; set; }

    public static PlayerState CreateRunner(string connectionId, int spawnX, int spawnY) => new()
    {
        ConnectionId = connectionId,
        Role = Role.Runner,
        X = spawnX,
        Y = spawnY,
        LivesRemaining = BalanceConstants.Match.PacmanLives,
    };

    public static PlayerState CreateHunter(string connectionId, int spawnX, int spawnY) => new()
    {
        ConnectionId = connectionId,
        Role = Role.Hunter,
        X = spawnX,
        Y = spawnY,
        LivesRemaining = 0, // unlimited respawns instead (FR-003)
    };
}
