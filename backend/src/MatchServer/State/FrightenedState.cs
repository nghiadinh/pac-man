using MatchServer.Generated;

namespace MatchServer.State;

/// <summary>
/// An active Frightened window (FR-005). Non-stackable: a second Power Pellet resets the timer
/// to a fresh 8.0s rather than extending it.
/// </summary>
public sealed class FrightenedState
{
    public required long StartedAtMs { get; set; }

    public long ExpiresAtMs => StartedAtMs + BalanceConstants.Frightened.FrightenedDurationMs;

    /// <summary>While elapsed is below this, Hunter directional input is inverted (FR-007).</summary>
    public long InversionExpiresAtMs =>
        StartedAtMs + BalanceConstants.Frightened.FrightenedInversionMs;

    public bool IsActiveAt(long elapsedMs) => elapsedMs < ExpiresAtMs;

    public bool IsInversionActiveAt(long elapsedMs) => elapsedMs < InversionExpiresAtMs;

    /// <summary>Restarts the window from now. FR-005: resets, never stacks.</summary>
    public void Reset(long elapsedMs) => StartedAtMs = elapsedMs;
}

/// <summary>Terminal result of a match. Never null once <see cref="MatchStatus.Ended"/> (SC-001).</summary>
public sealed class Outcome
{
    public required Role Winner { get; init; }

    public required MatchEndReason Reason { get; init; }

    public required int FinalPacmanScore { get; init; }

    public required int FinalGhostScore { get; init; }
}

/// <summary>
/// One point-earning action, folded into <see cref="PlayerState.Score"/> immediately and broadcast
/// to both clients (FR-019). Not retained as a list on the match.
/// </summary>
public sealed record ScoreEvent(ScoreEventType Type, int Points, Role Recipient);
