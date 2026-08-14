using MatchServer.Generated;

namespace MatchServer.State;

/// <summary>
/// Root aggregate for one game session - one MatchState per SignalR match Group. Holds exactly two
/// players, the timer, map/pellet state, and the outcome (data-model.md).
/// </summary>
public sealed class MatchState
{
    public required string MatchId { get; init; }

    public MatchStatus Status { get; set; } = MatchStatus.WaitingForPlayers;

    /// <summary>Server-ticked. The match ends when this reaches MatchDurationMs (FR-014).</summary>
    public long ElapsedMs { get; set; }

    public PlayerState? Pacman { get; set; }

    public PlayerState? Ghost { get; set; }

    public required MapState Map { get; init; }

    /// <summary>Present only while a Frightened window is active (FR-005).</summary>
    public FrightenedState? Frightened { get; set; }

    /// <summary>
    /// Consecutive-catch counter driving the 200/400/800/1600 sequence (FR-009). Resets to 0 when
    /// a Frightened window ends and when a new one begins.
    /// </summary>
    public int ScoreChain { get; set; }

    /// <summary>Set exactly once, when <see cref="Status"/> becomes Ended.</summary>
    public Outcome? Outcome { get; set; }

    public long RemainingMs => Math.Max(0, BalanceConstants.Match.MatchDurationMs - ElapsedMs);

    public bool BothPlayersJoined => Pacman is not null && Ghost is not null;

    public bool IsFrightenedActive =>
        Frightened is not null && Frightened.IsActiveAt(ElapsedMs);

    public bool IsInversionActive =>
        Frightened is not null && Frightened.IsInversionActiveAt(ElapsedMs);

    /// <summary>Ends the match with the given outcome. Idempotent - the first call wins, so a
    /// forfeit racing a natural end cannot overwrite an already-decided result.</summary>
    public void End(Role winner, MatchEndReason reason)
    {
        if (Status == MatchStatus.Ended)
        {
            return;
        }

        Status = MatchStatus.Ended;
        Outcome = new Outcome
        {
            Winner = winner,
            Reason = reason,
            FinalPacmanScore = Pacman?.Score ?? 0,
            FinalGhostScore = Ghost?.Score ?? 0,
        };
    }
}
